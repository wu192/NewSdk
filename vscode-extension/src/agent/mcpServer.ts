import * as http from 'node:http';
import {randomBytes, timingSafeEqual} from 'node:crypto';
import {AssetQueryAdapter, QUERY_TOOLS} from './queryAdapter';

export interface McpConnection {
    url: string;
    /** Secret for the current process lifetime; never persisted or logged by this server. */
    token: string;
    clientConfig: {type: 'http'; url: string; headers: {Authorization: string}};
}
interface RpcRequest {jsonrpc: '2.0'; id?: string | number | null; method: string; params?: Record<string, unknown>;}
class RpcError extends Error {constructor(readonly code: number, message: string) {super(message);}}
const protocolVersions = ['2025-06-18', '2025-03-26', '2024-11-05'];
const MAX_REQUEST_BYTES = 64 * 1024, MAX_RESPONSE_BYTES = 512 * 1024;

/** Opt-in, stateless Streamable HTTP MCP transport, bound only to IPv4 loopback.
 * No filesystem operation or edit tool is exposed. Each request uses the live editor service.
 */
export class ReadonlyMcpServer {
    private server?: http.Server;
    private connection?: McpConnection;
    private active = 0;
    private starting?: Promise<McpConnection>;
    private stopped = false;
    constructor(readonly adapter: AssetQueryAdapter) {}
    get running(): boolean {return !!this.connection;}
    start(): Promise<McpConnection> {
        if (this.connection) return Promise.resolve(this.connection);
        if (this.starting) return this.starting;
        this.stopped = false;
        return this.starting = new Promise<McpConnection>((resolve, reject) => {
            const token = randomBytes(32).toString('base64url');
            const server = http.createServer((request, response) => {void this.handle(request, response, token);});
            this.server = server;
            server.requestTimeout = 15_000; server.headersTimeout = 10_000; server.keepAliveTimeout = 1000;
            server.on('clientError', (_error, socket) => {socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');});
            server.once('error', error => {this.starting = undefined; this.server = undefined; reject(error);});
            server.listen(0, '127.0.0.1', () => {
                if (this.stopped) {server.close(); this.starting = undefined; reject(new Error('MCP start cancelled')); return;}
                const address = server.address();
                if (!address || typeof address === 'string') {server.close(); this.starting = undefined; reject(new Error('MCP loopback address unavailable')); return;}
                const url = `http://127.0.0.1:${address.port}/mcp`;
                this.connection = {url, token, clientConfig: {type: 'http', url, headers: {Authorization: `Bearer ${token}`}}};
                this.starting = undefined; resolve(this.connection);
            });
        });
    }
    private authorized(request: http.IncomingMessage, token: string): boolean {
        const actual = Buffer.from(request.headers.authorization ?? ''), expected = Buffer.from(`Bearer ${token}`);
        return actual.length === expected.length && timingSafeEqual(actual, expected);
    }
    private send(response: http.ServerResponse, status: number, value?: unknown): void {
        if (response.destroyed) return;
        const body = value === undefined ? '' : JSON.stringify(value);
        response.writeHead(status, {'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff'});
        response.end(body);
    }
    private async handle(request: http.IncomingMessage, response: http.ServerResponse, token: string): Promise<void> {
        // Reject browser requests and DNS rebinding, even when a client accidentally shares its bearer.
        const host = request.headers.host;
        const address = this.server?.address();
        const expectedHost = address && typeof address !== 'string' ? `127.0.0.1:${address.port}` : undefined;
        if (!host || host !== expectedHost || request.headers.origin !== undefined) {this.send(response, 403, {error: 'Loopback native clients only'}); return;}
        if (request.url !== '/mcp') {this.send(response, 404, {error: 'Not found'}); return;}
        if (!this.authorized(request, token)) {this.send(response, 401, {error: 'Bearer authorization required'}); return;}
        if (request.method !== 'POST') {response.setHeader('Allow', 'POST'); this.send(response, 405, {error: 'Use MCP JSON-RPC POST'}); return;}
        if (!/^application\/json(?:\s*;|$)/i.test(request.headers['content-type'] ?? '')) {this.send(response, 415, {error: 'Expected application/json'}); return;}
        if (this.active >= 4) {this.send(response, 429, {error: 'Too many active queries'}); return;}
        this.active++;
        let id: RpcRequest['id'];
        try {
            const body = await this.readBody(request);
            let raw: unknown; try {raw = JSON.parse(body);} catch {throw new RpcError(-32700, 'Parse error');}
            if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) throw new RpcError(-32600, 'A single JSON-RPC request is required');
            const rpc = raw as RpcRequest; id = rpc.id;
            if (rpc.jsonrpc !== '2.0' || typeof rpc.method !== 'string' || (id !== undefined && id !== null && typeof id !== 'string' && typeof id !== 'number') || (rpc.params !== undefined && (typeof rpc.params !== 'object' || rpc.params === null || Array.isArray(rpc.params)))) throw new RpcError(-32600, 'Invalid request');
            // Notifications perform no query work. No server-initiated messages are needed.
            if (id === undefined) {this.send(response, 202); return;}
            const result = await this.dispatch(rpc);
            const packet = {jsonrpc: '2.0', id, result};
            if (Buffer.byteLength(JSON.stringify(packet)) > MAX_RESPONSE_BYTES) throw new RpcError(-32602, 'Result exceeds response budget; request a smaller page');
            this.send(response, 200, packet);
        } catch (error) {
            if (error instanceof RpcError) this.send(response, error.code === -32001 ? 413 : 200, {jsonrpc: '2.0', id: id ?? null, error: {code: error.code, message: error.message}});
            else this.send(response, 200, {jsonrpc: '2.0', id: id ?? null, error: {code: -32603, message: 'Query unavailable; inspect project/index state in the editor.'}});
        } finally {this.active--;}
    }
    private readBody(request: http.IncomingMessage): Promise<string> {
        return new Promise((resolve, reject) => {
            const chunks: Buffer[] = []; let size = 0, done = false;
            const fail = (error: Error) => {if (done) return; done = true; clearTimeout(timer); reject(error);};
            const timer = setTimeout(() => {fail(new RpcError(-32001, 'Request body timed out')); request.destroy();}, 10_000);
            request.on('data', (chunk: Buffer) => {
                if (done) return;
                size += chunk.length;
                if (size > MAX_REQUEST_BYTES) {fail(new RpcError(-32001, 'Request body exceeds 64 KiB')); request.resume(); return;}
                chunks.push(chunk);
            });
            request.on('end', () => {if (done) return; done = true; clearTimeout(timer); resolve(Buffer.concat(chunks).toString('utf8'));});
            request.on('error', fail); request.on('aborted', () => fail(new Error('Request aborted')));
        });
    }
    private async dispatch(request: RpcRequest): Promise<unknown> {
        if (request.method === 'initialize') {
            const requested = request.params?.protocolVersion;
            return {protocolVersion: typeof requested === 'string' && protocolVersions.includes(requested) ? requested : protocolVersions[0],
                capabilities: {tools: {listChanged: false}}, serverInfo: {name: 'newsdk-readonly-xml', version: '1.0.0'},
                instructions: 'Read-only queries against the live NewSdk AssetService. Partial or unknown results are not validation success. XML changes require editor diff confirmation.'};
        }
        if (request.method === 'ping') return {};
        if (request.method === 'tools/list') return {tools: QUERY_TOOLS};
        if (request.method !== 'tools/call') throw new RpcError(-32601, 'Method not found');
        const name = request.params?.name, args = request.params?.arguments ?? {};
        if (typeof name !== 'string' || !QUERY_TOOLS.some(tool => tool.name === name)) throw new RpcError(-32602, 'Unknown read-only query tool');
        if (typeof args !== 'object' || args === null || Array.isArray(args)) throw new RpcError(-32602, 'arguments must be an object');
        try {
            const data = await this.adapter.query(name, args as Record<string, unknown>);
            return {content: [{type: 'text', text: JSON.stringify(data)}], structuredContent: data, isError: false};
        } catch {
            // Do not include filesystem errors containing absolute host paths or XML source in protocol errors.
            return {content: [{type: 'text', text: 'Query rejected or unavailable. Check arguments and the current project/index status in the editor.'}], isError: true};
        }
    }
    async stop(): Promise<void> {
        this.stopped = true; this.connection = undefined;
        const server = this.server; this.server = undefined;
        if (!server) return;
        await new Promise<void>(resolve => {server.close(() => resolve()); server.closeAllConnections();});
    }
    dispose(): void {void this.stop();}
}
