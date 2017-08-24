using NLog.Common;
using NLog.Config;
using SharpRaven;
using SharpRaven.Data;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Reflection;

// ReSharper disable CheckNamespace

namespace NLog.Targets
    // ReSharper restore CheckNamespace
{
    [Target("Sentry")]
    public class SentryTarget : TargetWithLayout
    {
        private Dsn dsn;
        private readonly Lazy<IRavenClient> client;
        private static readonly string RootAssemblyVersion;

        /// <summary>
        /// Map of NLog log levels to Raven/Sentry log levels
        /// </summary>
        protected static readonly IDictionary<LogLevel, ErrorLevel> LoggingLevelMap = new Dictionary<LogLevel, ErrorLevel>
        {
            { LogLevel.Debug, ErrorLevel.Debug },
            { LogLevel.Error, ErrorLevel.Error },
            { LogLevel.Fatal, ErrorLevel.Fatal },
            { LogLevel.Info, ErrorLevel.Info },
            { LogLevel.Trace, ErrorLevel.Debug },
            { LogLevel.Warn, ErrorLevel.Warning },
        };

        static SentryTarget()
        {
            var entryAssembly = Assembly.GetEntryAssembly();

            if (null != entryAssembly)
            {
                RootAssemblyVersion = entryAssembly.GetName()
                                                   .Version.ToString();
                return;
            }

            try
            {
                // ReSharper disable PossibleNullReferenceException
                var systemWebAssembly = AppDomain.CurrentDomain.GetAssemblies()
                                                 .SingleOrDefault(e => e.FullName.StartsWith("System.Web,") && e.FullName.Contains("PublicKeyToken=b03f5f7f11d50a3a"));
                var httpContext = systemWebAssembly.ExportedTypes.SingleOrDefault(e => "HttpContext" == e.Name && "System.Web" == e.Namespace);
                var currentContextProperty = httpContext.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                var currentContext = currentContextProperty.GetValue(null);
                var appInstanceProperty = currentContextProperty.PropertyType.GetProperty("ApplicationInstance");
                var appInstance = appInstanceProperty.GetValue(currentContext);
                var appInstanceType = appInstance.GetType();
                var appInstanceBaseType = appInstanceType.BaseType;
                var rootAssemblyName = appInstanceBaseType.Assembly.GetName();
                RootAssemblyVersion = rootAssemblyName.Version.ToString();
                // ReSharper restore PossibleNullReferenceException
            }
            catch (NullReferenceException)
            {
                // If we could not find the web assembly or any of the properties that we needed to access to get the root assembly version then just return
                // because we have done all we can do.
            }
        }

        /// <summary>
        /// The DSN for the Sentry host
        /// </summary>
        [RequiredParameter]
        public string Dsn
        {
            get { return this.dsn?.ToString(); }
            set { this.dsn = new Dsn(value); }
        }

        /// <summary>
        /// Determines whether events with no exceptions will be send to Sentry or not
        /// </summary>
        public bool IgnoreEventsWithNoException { get; set; }

        /// <summary>
        /// Determines whether event properties will be sent to sentry as Tags or not
        /// </summary>
        public bool SendLogEventInfoPropertiesAsTags { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public SentryTarget()
        {
            this.client = new Lazy<IRavenClient>(this.DefaultClientFactory);
        }

        /// <summary>
        /// Internal constructor, used for unit-testing
        /// </summary>
        /// <param name="ravenClient">A <see cref="IRavenClient"/></param>
        internal SentryTarget(IRavenClient ravenClient)
        {
            this.client = new Lazy<IRavenClient>(() => ravenClient);
        }

        /// <summary>
        /// Writes logging event to the log target.
        /// </summary>
        /// <param name="logEvent">Logging event to be written out.</param>
        protected override void Write(LogEventInfo logEvent)
        {
            try
            {
                var tags = this.SendLogEventInfoPropertiesAsTags
                               ? logEvent.Properties.ToDictionary(x => x.Key.ToString(), x => x.Value.ToString())
                               : null;

                var extras = this.SendLogEventInfoPropertiesAsTags
                                 ? null
                                 : logEvent.Properties.ToDictionary(x => x.Key.ToString(), x => x.Value.ToString());

                this.client.Value.Logger = logEvent.LoggerName;

                // If the log event did not contain an exception and we're not ignoring
                // those kinds of events then we'll send a "Message" to Sentry
                if (logEvent.Exception == null && !this.IgnoreEventsWithNoException)
                {
                    var sentryMessage = new SentryMessage(this.Layout.Render(logEvent));
                    var msg = new SentryEvent(sentryMessage)
                    {
                        Level = LoggingLevelMap[logEvent.Level],
                        Extra = extras,
                        Tags = tags
                    };
                    this.client.Value.Capture(msg);
                }
                else if (logEvent.Exception != null)
                {
                    var sentryMessage = new SentryMessage(logEvent.FormattedMessage);
                    var sentryEvent = new SentryEvent(logEvent.Exception)
                    {
                        Extra = extras,
                        Level = LoggingLevelMap[logEvent.Level],
                        Message = sentryMessage,
                        Tags = tags
                    };
                    this.client.Value.Capture(sentryEvent);
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && this.client.IsValueCreated)
            {
                var ravenClient = this.client.Value as RavenClient;

                if (ravenClient != null)
                {
                    ravenClient.ErrorOnCapture = null;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Implements the default client factory behavior.
        /// </summary>
        /// <returns>New instance of a RavenClient.</returns>
        private IRavenClient DefaultClientFactory()
        {
            var ravenClient = new RavenClient(this.dsn)
            {
                ErrorOnCapture = this.LogException
            };

            string timeoutSetting = ConfigurationManager.AppSettings["RavenClient.Timeout"];
            TimeSpan timeout;

            if ((false == string.IsNullOrWhiteSpace(timeoutSetting)) && TimeSpan.TryParseExact(timeoutSetting, "c", CultureInfo.InvariantCulture, out timeout))
            {
                ravenClient.Timeout = timeout;
            }

            ravenClient.Release = RootAssemblyVersion;
            string environment = ConfigurationManager.AppSettings["RavenClient.Environment"];

            if (false == string.IsNullOrWhiteSpace(environment))
            {
                ravenClient.Environment = environment;
            }

            return ravenClient;
        }

        private void LogException(Exception ex)
        {
            InternalLogger.Error("Unable to send Sentry request: {0}", ex.Message);
        }
    }
}
