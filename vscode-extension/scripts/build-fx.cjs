const esbuild = require('esbuild');
const fs = require('node:fs');
const path = require('node:path');
async function main() {
  const entry = path.join(__dirname, '../webview/fx/main.ts');
  await esbuild.build({entryPoints: [entry], bundle: true, outfile: path.join(__dirname, '../media/fx-editor.js'), platform: 'browser', target: ['chrome120'], minify: true, legalComments: 'none'});
  fs.copyFileSync(path.join(__dirname, '../webview/fx/fx.css'), path.join(__dirname, '../media/fx.css'));
  const checker = path.join(__dirname, '../out/src/fx/check-xsd.py');
  fs.mkdirSync(path.dirname(checker), {recursive: true});
  fs.copyFileSync(path.join(__dirname, 'check-xsd.py'), checker);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
