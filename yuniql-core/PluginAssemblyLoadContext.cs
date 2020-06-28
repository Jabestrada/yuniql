﻿using System;
using System.Reflection;
using System.Runtime.Loader;
using Yuniql.Extensibility;

namespace Yuniql.Core
{
    //https://github.com/dotnet/samples/tree/master/core/tutorials/Unloading
    /// <summary>
    /// This is a collectible (unloadable) AssemblyLoadContext that loads the dependencies
    /// of the plugin from the plugin's binary directory.
    /// </summary>
    public class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        // Resolver of the locations of the assemblies that are dependencies of the
        private AssemblyDependencyResolver _resolver;
        private readonly ITraceService _traceService;
        private AssemblyLoadContext _defaultLoadContext;
        private Exception _pluginLoadException;

        /// <summary>
        /// The location of the plugin binaries
        /// </summary>
        public string PluginPath { get; set; }

        /// <inheritdoc />
        public PluginAssemblyLoadContext(
            AssemblyLoadContext defaultLoadContext,
            string pluginAssemblyFilePath,
            ITraceService traceService) : base(isCollectible: true)
        {
            _defaultLoadContext = defaultLoadContext;
            PluginPath = pluginAssemblyFilePath;
            this._traceService = traceService;
            try {
                this._resolver = new AssemblyDependencyResolver(pluginAssemblyFilePath);
            }
            catch(Exception exc) {
                // Suppress exception; signal error, if needed, during Load()
                _pluginLoadException = exc; 
            }
        }

        /// <summary>
        /// The Load method override causes all the dependencies present in the plugin's binary directory to get loaded
        /// into the HostAssemblyLoadContext together with the plugin assembly itself.
        /// The Interface assembly must not be present in the plugin's binary directory, otherwise we would
        /// end up with the assembly being loaded twice. Once in the default context and once in the HostAssemblyLoadContext.
        /// </summary>
        /// <param name="assemblyName">The assembly name to load.</param>
        /// <returns></returns>
        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (null == assemblyName.Name) return null;

            try
            {
                var assembly = _defaultLoadContext.LoadFromAssemblyName(assemblyName);
                if (null != assembly)
                {
                    _traceService.Debug($"Resolved dependency: {assemblyName.Name}, v{assemblyName.Version} from DefaultLoadContext");

                    return assembly;
                }
            }
            catch (Exception exc)
            {
                _traceService.Debug($"Error while attempting to resolve  {assemblyName.Name}, v{assemblyName.Version} from DefaultContext: {exc.Message}");
                _traceService.Debug($"Resolving dependency: {assemblyName.Name}, v{assemblyName.Version} from componentAssemblyPath: {PluginPath}");

                if (_resolver == null) {
                    throw new Exception($"Cannot fallback to componentAssemblyPath because resolver for {PluginPath} was null: {_pluginLoadException}");
                }
                

                string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

                if (assemblyPath != null)
                {
                    _traceService.Debug($"Resolved dependency. Loading {assemblyPath} into the PluginAssemblyLoadContext");
                    return LoadFromAssemblyPath(assemblyPath);
                }
                else
                {
                    _traceService.Debug($"Failed resolving dependency: {assemblyName.Name}, v{assemblyName.Version}");
                }
            }

            return null;
        }

        /// <inheritdoc />
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            _traceService.Debug($"Resolving unmanaged dependency: {unmanagedDllName} from componentAssemblyPath: {PluginPath}");

            if (libraryPath != null)
            {
                _traceService.Debug($"Resolved unmanaged dependency. Loading {libraryPath} into the PluginAssemblyLoadContext");
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}

