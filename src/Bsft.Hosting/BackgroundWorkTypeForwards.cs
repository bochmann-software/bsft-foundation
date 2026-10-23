using System.Runtime.CompilerServices;
using Bsft.Hosting.BackgroundWork;

// The run-once types were defined in Bsft.Hosting in older releases (Foundation 2.1.0 among them) and now live in
// Bsft.Hosting.BackgroundWork, with the same names and namespaces. A binary compiled against the older Bsft.Hosting
// references them as "[Bsft.Hosting]Bsft.Hosting.BackgroundWork.X"; these forwards let the runtime resolve that
// reference to the new assembly instead of failing with a TypeLoadException. The registration class
// BackgroundWorkServiceCollectionExtensions changed namespace as well, which a forward cannot express; its callers
// recompile against Bsft.Hosting.BackgroundWork.DependencyInjection (docs/migration-2.2.0.md).
[assembly: TypeForwardedTo(typeof(BsftRunOnceOptions))]
[assembly: TypeForwardedTo(typeof(RunOnceWithRetryBackgroundServiceBase))]
[assembly: TypeForwardedTo(typeof(ServiceScopeFactoryExtensions))]
