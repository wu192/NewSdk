using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;

namespace BinaryAssetBuilder.Core;

public class Tracer
{
	private class InternalScopedTrace : IDisposable
	{
		private Tracer _Tracer;

		private string _Text;

		public InternalScopedTrace(Tracer tracer, string text)
		{
			_Text = text;
			_Tracer = tracer;
			_Tracer.Output(TraceEventType.Verbose, "Entering: {0}", _Text);
			IndentLevel++;
		}

		public void Dispose()
		{
			IndentLevel--;
			_Tracer.Output(TraceEventType.Verbose, "Leaving: {0}", _Text);
		}
	}

	public static TraceKind[] VerbosityLevels;

	private static Dictionary<string, Tracer> _Tracers;

	public static TraceWriteHandler TraceWrite;

	private static LocalDataStoreSlot _IndentLevelStore;

	private static TraceKind _TraceMask;

	[ThreadStatic]
	private static DiagnosticCapture _DiagnosticCapture;

	// Each worker owns its diagnostic list. Delivery stays on the owner thread,
	// where the build's trace callback and document context are installed.
	public sealed class DiagnosticCapture : IDisposable
	{
		private sealed class Entry
		{
			internal string Source, Message;
			internal TraceEventType Type;
		}

		private readonly DiagnosticCapture parent;
		private readonly int threadId;
		private readonly List<Entry> entries = new List<Entry>();
		private int replayed;
		private bool disposed;

		internal DiagnosticCapture()
		{
			parent = _DiagnosticCapture;
			threadId = Thread.CurrentThread.ManagedThreadId;
			_DiagnosticCapture = this;
		}

		internal void Add(string source, TraceEventType type, string message)
		{
			entries.Add(new Entry { Source = source, Type = type, Message = message });
		}

		public void Dispose()
		{
			if (disposed) return;
			if (threadId != Thread.CurrentThread.ManagedThreadId || !ReferenceEquals(_DiagnosticCapture, this))
				throw new InvalidOperationException("Diagnostic captures must be disposed on their creating thread in reverse order.");
			_DiagnosticCapture = parent;
			disposed = true;
		}

		public void Replay()
		{
			if (!disposed) throw new InvalidOperationException("Complete the worker diagnostic capture before replaying it.");
			while (replayed < entries.Count)
			{
				Entry entry = entries[replayed++];
				OnWrite(entry.Source, entry.Type, entry.Message);
			}
			entries.Clear();
			replayed = 0;
		}
	}

	public static DiagnosticCapture CaptureDiagnostics() => new DiagnosticCapture();

	private string _Name;

	private string _Description;

	private bool _TraceEnabled;

	private static int IndentLevel
	{
		get
		{
			int result = 0;
			object data = Thread.GetData(_IndentLevelStore);
			if (data == null)
			{
				Thread.SetData(_IndentLevelStore, 0);
			}
			else
			{
				result = (int)data;
			}
			return result;
		}
		set
		{
			Thread.SetData(_IndentLevelStore, value);
		}
	}

	private TraceKind Options => _TraceMask;

	static Tracer()
	{
		VerbosityLevels = new TraceKind[10]
		{
			TraceKind.None,
			TraceKind.Exception | TraceKind.Assert | TraceKind.Error | TraceKind.Warning | TraceKind.Message,
			TraceKind.Exception | TraceKind.Assert | TraceKind.Error | TraceKind.Warning | TraceKind.Message,
			(TraceKind)255,
			(TraceKind)255,
			(TraceKind)255,
			(TraceKind)511,
			(TraceKind)511,
			(TraceKind)511,
			TraceKind.All
		};
		_Tracers = new Dictionary<string, Tracer>();
		_IndentLevelStore = Thread.AllocateDataSlot();
		_TraceMask = VerbosityLevels[1];
	}

	public static Tracer GetTracer(string name, string description)
	{
		lock (_Tracers)
		{
			Tracer value;
			if (!_Tracers.TryGetValue(name, out value))
			{
				value = new Tracer(name, description);
				_Tracers.Add(name, value);
			}
			return value;
		}
	}

	public static void SetTraceLevel(int level)
	{
		_TraceMask = VerbosityLevels[level];
	}

    private static readonly object ScopeGate = new object();
    private static TraceScope _CurrentScope;

    // Replace rather than append the callback: one diagnostic must be delivered
    // once, and the caller's exact mask/delegate must survive a failed stream.
    public static IDisposable BeginScope(TraceWriteHandler writer, int traceLevel)
    {
        lock (ScopeGate)
        {
            TraceKind mask = VerbosityLevels[traceLevel];
            EnsureScopeThread();
            var scope = new TraceScope();
            TraceWrite = writer;
            _TraceMask = mask;
            _CurrentScope = scope;
            return scope;
        }
    }

    private static void EnsureScopeThread()
    {
        if (_CurrentScope != null && _CurrentScope.ThreadId != Thread.CurrentThread.ManagedThreadId)
            throw new InvalidOperationException("Trace scopes may only be used on their owning thread.");
    }

    private sealed class TraceScope : IDisposable
    {
        internal readonly int ThreadId = Thread.CurrentThread.ManagedThreadId;
        private readonly TraceScope parent = _CurrentScope;
        private readonly TraceWriteHandler writer = TraceWrite;
        private readonly TraceKind mask = _TraceMask;
        private bool disposed;

        public void Dispose()
        {
            lock (ScopeGate)
            {
                if (disposed) return;
                EnsureScopeThread();
                if (!Object.ReferenceEquals(_CurrentScope, this))
                    throw new InvalidOperationException("Trace scopes must be disposed in reverse creation order.");
                TraceWrite = writer;
                _TraceMask = mask;
                _CurrentScope = parent;
                disposed = true;
            }
        }
    }

	private static void OnWrite(string source, TraceEventType type, string message)
	{
		if (_DiagnosticCapture != null)
		{
			_DiagnosticCapture.Add(source, type, message);
			return;
		}
		if (TraceWrite != null)
		{
			TraceWrite(source, type, message);
		}
	}

	private void Output(TraceEventType type, string format, params object[] args)
	{
		// An explicit build scope owns diagnostic delivery even for Tracers
		// created earlier while the legacy BabEnableTrace switch was enabled.
		// Keep that legacy branch unchanged when no scope is installed.
		if (_TraceEnabled && _CurrentScope == null && _DiagnosticCapture == null)
		{
			StringBuilder stringBuilder = new StringBuilder(new string(' ', IndentLevel * Trace.IndentSize));
			stringBuilder.AppendFormat(format, args);
		}
		else
		{
			OnWrite(_Name, type, string.Format(format, args));
		}
	}

	private Tracer(string name, string description)
	{
		_Name = name;
		_Description = description;
		_TraceEnabled = false;
		string environmentVariable = Environment.GetEnvironmentVariable("BabEnableTrace");
		_TraceEnabled = string.Equals(environmentVariable, "True", StringComparison.InvariantCultureIgnoreCase);
	}

	private static string GetCallingMethodInfo(string additionalInfo)
	{
		StackFrame stackFrame = new StackFrame(2);
		MethodBase method = stackFrame.GetMethod();
		return $"{method.DeclaringType.Name}.{method.Name}({additionalInfo})";
	}

	public IDisposable TraceMethod()
	{
		if ((_TraceMask & TraceKind.Method) == 0)
		{
			return null;
		}
		return new InternalScopedTrace(this, GetCallingMethodInfo(string.Empty));
	}

	public IDisposable TraceMethod(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Method) == 0)
		{
			return null;
		}
		return new InternalScopedTrace(this, GetCallingMethodInfo(string.Format(format, args)));
	}

	public void Message(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Message) != TraceKind.None)
		{
			Output(TraceEventType.Information, format, args);
		}
	}

	public void TraceData(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Data) != TraceKind.None)
		{
			Output(TraceEventType.Verbose, format, args);
		}
	}

	public void TraceInfo(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Info) != TraceKind.None)
		{
			Output(TraceEventType.Information, format, args);
		}
	}

	public void TraceNote(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Note) != TraceKind.None)
		{
			Output(TraceEventType.Verbose, format, args);
		}
	}

	public void TraceError(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Error) != TraceKind.None)
		{
			Output(TraceEventType.Error, format, args);
		}
	}

	public void TraceWarning(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Warning) != TraceKind.None)
		{
			Output(TraceEventType.Warning, format, args);
		}
	}

	public void TraceException(string format, params object[] args)
	{
		if ((_TraceMask & TraceKind.Exception) != TraceKind.None)
		{
			Output(TraceEventType.Critical, format, args);
		}
	}
}
