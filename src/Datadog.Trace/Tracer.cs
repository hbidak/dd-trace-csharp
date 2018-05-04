using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Datadog.Trace.Agent;
using Datadog.Trace.Logging;

namespace Datadog.Trace
{
    /// <summary>
    /// The tracer is responsible for creating spans and flushing them to the Datadog agent
    /// </summary>
    public class Tracer : IDatadogTracer
    {
        private const string UnknownServiceName = "UnknownService";
        private static readonly ILog _log = LogProvider.For<Tracer>();

        private static Lazy<Tracer> _defaultInstance;
        private static Tracer _instance;

        // TODO: IScopeManager
        private AsyncLocalScopeManager _scopeManager;

        private string _defaultServiceName;
        private IAgentWriter _agentWriter;

        public static string DefaultAgentUri => "http://localhost:8126";

        static Tracer()
        {
            _defaultInstance = new Lazy<Tracer>(LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Tracer()
            : this(DefaultAgentUri)
        {
        }

        public Tracer(string uri)
            : this(uri, Assembly.GetEntryAssembly().GetName().Name)
        {
        }

        public Tracer(string uri, string defaultServiceName)
            : this(new AgentWriter(new Api(new Uri(uri))), defaultServiceName)
        {
        }

        public Tracer(IAgentWriter agentWriter)
            : this(agentWriter, Assembly.GetEntryAssembly().GetName().Name)
        {
        }

        public Tracer(IAgentWriter agentWriter, string defaultServiceName)
        {
            _agentWriter = agentWriter;
            _defaultServiceName = defaultServiceName;
            _scopeManager = new AsyncLocalScopeManager();
        }

        /// <summary>
        /// Gets the global tracer object
        /// </summary>
        public static Tracer Instance => _instance ?? _defaultInstance.Value;

        /// <summary>
        /// Gets the active scope
        /// </summary>
        public Scope ActiveScope => _scopeManager.Active;

        string IDatadogTracer.DefaultServiceName => _defaultServiceName;

        /// <summary>
        /// Writes the specified <see cref="Span"/> collection to the agent writer.
        /// </summary>
        /// <param name="trace">The <see cref="Span"/> collection to write.</param>
        void IDatadogTracer.Write(List<Span> trace)
        {
            _agentWriter.WriteTrace(trace);
        }

        public static void RegisterInstance(Tracer tracer)
        {
            _instance = tracer;
        }

        /// <summary>
        /// Make a span active and return a scope that can be disposed to desactivate the span
        /// </summary>
        /// <param name="span">The span to activate</param>
        /// <param name="finishOnClose">If set to false, closing the returned scope will not close the enclosed span </param>
        /// <returns>A Scope object wrapping this span</returns>
        public Scope ActivateSpan(Span span, bool finishOnClose = true)
        {
            return _scopeManager.Activate(span, finishOnClose);
        }

        /// <summary>
        /// This is a shortcut for <see cref="StartSpan"/> and <see cref="ActivateSpan"/>, it creates a new span with the given parameters and makes it active.
        /// </summary>
        /// <param name="operationName">The span's operation name</param>
        /// <param name="childOf">The span's parent</param>
        /// <param name="serviceName">The span's service name</param>
        /// <param name="startTime">An explicit start time for that span</param>
        /// <param name="ignoreActiveScope">If set the span will not be a child of the currently active span</param>
        /// <param name="finishOnClose">If set to false, closing the returned scope will not close the enclosed span </param>
        /// <returns>A scope wrapping the newly created span</returns>
        public Scope StartActive(string operationName, SpanContext childOf = null, string serviceName = null, DateTimeOffset? startTime = null, bool ignoreActiveScope = false, bool finishOnClose = true)
        {
            var span = StartSpan(operationName, childOf, serviceName, startTime, ignoreActiveScope);
            return _scopeManager.Activate(span, finishOnClose);
        }

        /// <summary>
        /// This create a Span with the given parameters
        /// </summary>
        /// <param name="operationName">The span's operation name</param>
        /// <param name="childOf">The span's parent</param>
        /// <param name="serviceName">The span's service name</param>
        /// <param name="startTime">An explicit start time for that span</param>
        /// <param name="ignoreActiveScope">If set the span will not be a child of the currently active span</param>
        /// <returns>The newly created span</returns>
        public Span StartSpan(string operationName, SpanContext childOf = null, string serviceName = null, DateTimeOffset? startTime = null, bool ignoreActiveScope = false)
        {
            if (childOf == null && !ignoreActiveScope)
            {
                childOf = _scopeManager.Active?.Span?.Context;
            }

            var span = new Span(this, childOf, operationName, serviceName, startTime);
            span.TraceContext.AddSpan(span);
            return span;
        }

        /// <summary>
        /// Writes the specified <see cref="Span"/> collection to the agent writer.
        /// </summary>
        /// <param name="trace">The <see cref="Span"/> collection to write.</param>
        public async Task FlushTracesAsync()
        {
            await _agentWriter.FlushAsync();
        }
    }
}