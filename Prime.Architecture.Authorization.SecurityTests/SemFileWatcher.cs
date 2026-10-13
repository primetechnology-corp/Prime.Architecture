using System;
using System.Runtime.CompilerServices;

namespace Prime.Architecture.Authorization.SecurityTests;

internal static class SemFileWatcher
{
    // Cada servidor de teste criaria um observador de arquivos (inotify); o Linux limita isso a 128 por usuário.
    [ModuleInitializer]
    internal static void Inicializar()
        => Environment.SetEnvironmentVariable("DOTNET_hostBuilder:reloadConfigOnChange", "false");
}
