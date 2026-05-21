using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

internal static partial class Tests
{
    public static Task VersionMetadataIs335()
    {
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "OpenClaw.csproj");
        var packageManifestPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Package.appxmanifest");
        var appManifestPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "app.manifest");
        var aboutPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "AboutDialog.xaml.cs");

        var project = File.ReadAllText(projectPath);
        var packageManifest = File.ReadAllText(packageManifestPath);
        var appManifest = File.ReadAllText(appManifestPath);
        var about = File.ReadAllText(aboutPath);

        Assert.Contains("<Version>3.3.5</Version>", project, "Project package version should be 3.3.5.");
        Assert.Contains("<AssemblyVersion>3.3.5.0</AssemblyVersion>", project, "Assembly version should be 3.3.5.0.");
        Assert.Contains("<FileVersion>3.3.5.0</FileVersion>", project, "File version should be 3.3.5.0.");
        Assert.Contains("Version=\"3.3.5.0\"", packageManifest, "Package manifest version should be 3.3.5.0.");
        Assert.Contains("version=\"3.3.5.0\"", appManifest, "Application manifest assembly identity should be 3.3.5.0.");
        Assert.Contains("AppMetadata.GetDisplayVersion()", about, "About dialog should display the assembly-backed app version.");
        return Task.CompletedTask;
    }

    public static Task RepositoryCodeStyleIsExplicit()
    {
        var editorConfigPath = Path.Combine(Directory.GetCurrentDirectory(), ".editorconfig");
        Assert.True(File.Exists(editorConfigPath), "Repository should define a root .editorconfig.");

        var editorConfig = File.ReadAllText(editorConfigPath);
        Assert.Contains("root = true", editorConfig, ".editorconfig should be the root style source.");
        Assert.Contains("end_of_line = lf", editorConfig, "Source files should default to Linux-style LF endings.");
        Assert.Contains("insert_final_newline = true", editorConfig, "Files should end with one final newline.");
        Assert.Contains("trim_trailing_whitespace = true", editorConfig, "Trailing whitespace should be rejected by formatter verification.");
        Assert.Contains("indent_size = 4", editorConfig, "C# and XAML indentation should be four spaces.");
        Assert.Contains("csharp_prefer_braces = true", editorConfig, "Control flow should use braces consistently.");
        Assert.Contains("dotnet_diagnostic.IDE0055.severity", editorConfig, "Formatting diagnostics should be explicit.");
        return Task.CompletedTask;
    }

    public static Task CodeStyleGuideDocumentsProjectConventions()
    {
        var guidePath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "code-style.md");
        Assert.True(File.Exists(guidePath), "Repository should document project-specific code style in docs/code-style.md.");

        var guide = File.ReadAllText(guidePath);
        var readme = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "README.md"));
        var readmeZh = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "readme_zh.md"));
        var developmentNotes = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "DEVELOPMENT_NOTES.md"));

        Assert.Contains("docs/code-style.md", readme, "README should link the code style guide.");
        Assert.Contains("docs/code-style.md", readmeZh, "Chinese README should link the code style guide.");
        Assert.Contains("Project Code Standards", developmentNotes, "Development notes should keep the code standards entry point.");
        Assert.Contains("MainWindow", guide, "Guide should cover shell partial ownership.");
        Assert.Contains("MainViewModel", guide, "Guide should cover view-model partial ownership.");
        Assert.Contains("ShellSessionCoordinator", guide, "Guide should cover coordinator partial ownership.");
        Assert.Contains("WebViewService", guide, "Guide should warn against growing the largest WebView service further.");
        Assert.Contains("HostedUiBridge.Script.cs", guide, "Guide should keep bridge script content behind its script-builder seam.");
        Assert.Contains("HostedUiBridge.Script.js", guide, "Guide should keep browser bridge implementation in a runnable JS asset.");
        Assert.Contains("dotnet format OpenClaw.sln --verify-no-changes --no-restore", guide, "Guide should document the format gate.");
        Assert.Contains("dotnet run --project tests\\OpenClaw.Tests\\OpenClaw.Tests.csproj -c Debug --no-restore", guide, "Guide should document the executable harness command.");
        return Task.CompletedTask;
    }

    public static Task ArchitectureGuidePreservesCurrentModuleBoundaries()
    {
        var guidePath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "code-style.md");
        Assert.True(File.Exists(guidePath), "Repository should document architecture boundaries in docs/code-style.md.");

        var guide = File.ReadAllText(guidePath);
        Assert.Contains("WinUI layer", guide, "Guide should name the WinUI layer boundary.");
        Assert.Contains("Core physical source tree", guide, "Guide should name the Core physical source boundary.");
        Assert.Contains("There are no current linked Core source exceptions", guide, "Guide should state the current Core source tree has no linked exceptions.");
        Assert.Contains("partial files are split by responsibility", guide, "Guide should describe partial-file ownership.");
        Assert.Contains("new protocol or parser code starts in Core-compatible files", guide, "Guide should route pure protocol/parser code to Core-compatible files.");
        return Task.CompletedTask;
    }

    public static Task CoreCompatibleFilesArePhysicallyOwnedByCoreProject()
    {
        var appRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw");
        var coreRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core");
        var coreProjectPath = Path.Combine(coreRoot, "OpenClaw.Core.csproj");
        var appProjectPath = Path.Combine(appRoot, "OpenClaw.csproj");
        var coreProject = File.ReadAllText(coreProjectPath);
        var appProject = File.ReadAllText(appProjectPath);

        var physicallyMovedFiles = new[]
        {
            Path.Combine("Helpers", "AtomicFileWriter.cs"),
            Path.Combine("Helpers", "LogFileUtilities.cs"),
            Path.Combine("Helpers", "WindowBoundsUtilities.cs"),
            Path.Combine("Models", "AppSettings.cs"),
            Path.Combine("Models", "EnvironmentConfig.cs"),
            Path.Combine("Models", "RecoveryModels.cs"),
            Path.Combine("Models", "RecoveryPolicyOptions.cs"),
            Path.Combine("Services", "AppTelemetry.cs"),
            Path.Combine("Services", "CloudflareRayParser.cs"),
            Path.Combine("Services", "ConfigurationService.cs"),
            Path.Combine("Services", "ControlUiLatencyService.cs"),
            Path.Combine("Services", "DiagnosticBundleService.cs"),
            Path.Combine("Services", "HotkeyBinding.cs"),
            Path.Combine("Services", "IAppLogger.cs"),
            Path.Combine("Services", "LatencyHistory.cs"),
            Path.Combine("Services", "LoggingService.cs"),
            Path.Combine("Services", "SessionProbeModels.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Attach.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Dependencies.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.EventHandlers.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Events.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Helpers.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Host.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Recovery.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.RecoveryInspection.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.RecoveryLifecycle.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.RecoveryStateTransitions.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.State.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.StateEffects.cs"),
            Path.Combine("Services", "ShellSessionCoordinator.Telemetry.cs"),
            Path.Combine("Services", "SingleInstanceCoordinator.cs"),
            Path.Combine("Services", "TrayClosePolicy.cs"),
            Path.Combine("Services", "TrayMenuStrings.cs"),
            Path.Combine("Services", "WebViewCircuitBreaker.cs"),
        };

        foreach (var relativePath in physicallyMovedFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(coreRoot, relativePath)),
                $"{relativePath} should physically live in OpenClaw.Core.");
            Assert.False(
                File.Exists(Path.Combine(appRoot, relativePath)),
                $"{relativePath} should no longer physically live in the WinUI project tree.");
            Assert.DoesNotContain($"..\\OpenClaw\\{relativePath}", coreProject, $"{relativePath} should not be linked from the WinUI project tree.");
            Assert.DoesNotContain($"Compile Remove=\"{relativePath}\"", appProject, $"{relativePath} should not need a stale app-project Compile Remove after moving to Core.");
        }

        Assert.DoesNotContain("..\\OpenClaw\\", coreProject, "Core-compatible source files should physically live in OpenClaw.Core instead of being linked from the WinUI project tree.");
        Assert.Contains("ProjectReference Include=\"..\\OpenClaw.Core\\OpenClaw.Core.csproj\"", appProject, "The WinUI app should consume Core through a project reference.");
        Assert.False(
            File.Exists(Path.Combine(coreRoot, "Services", "ShellSessionCoordinator.Adapters.cs")),
            "App-local ShellSessionCoordinator adapters should stay in the WinUI project.");
        return Task.CompletedTask;
    }

    public static Task DirectoryBuildEnablesAnalyzersAndStyle()
    {
        var propsPath = Path.Combine(Directory.GetCurrentDirectory(), "Directory.Build.props");
        var props = File.ReadAllText(propsPath);

        Assert.Contains("<EnableNETAnalyzers>true</EnableNETAnalyzers>", props, "Shared build props should enable .NET analyzers.");
        Assert.Contains("<AnalysisLevel>latest</AnalysisLevel>", props, "Analyzer level should follow the SDK used by the repo.");
        Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", props, "Builds should honor .editorconfig code-style severities.");
        Assert.Contains("<Nullable>enable</Nullable>", props, "Nullable analysis should be project-wide by default.");
        Assert.Contains("<ImplicitUsings>enable</ImplicitUsings>", props, "Implicit usings should be project-wide by default.");
        return Task.CompletedTask;
    }

    public static Task ExecutableTestHarnessRejectsDotnetTestFalsePositives()
    {
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "tests",
            "OpenClaw.Tests",
            "OpenClaw.Tests.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("FailDotNetTestForExecutableHarness", project, "Executable test harness should not let dotnet test report a false green run.");
        Assert.Contains("dotnet run --project tests\\OpenClaw.Tests\\OpenClaw.Tests.csproj", project, "The dotnet test error should point to the real test command.");
        Assert.Contains("BeforeTargets=\"VSTest\"", project, "The guard should run when dotnet test invokes the VSTest target.");
        return Task.CompletedTask;
    }

    public static Task TestHarnessIsSplitByDomain()
    {
        var testRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "OpenClaw.Tests");
        var programPath = Path.Combine(testRoot, "Program.cs");
        var expectedFiles = new[]
        {
            "Tests.Recovery.cs",
            "Tests.Settings.cs",
            "Tests.StyleArchitecture.cs",
            "Tests.TrayAndDiagnostics.cs",
            "Tests.HostedBridge.cs",
            "Tests.Platform.cs",
            "Tests.ShellAndWebView.cs",
            "Tests.Support.cs"
        };

        foreach (var fileName in expectedFiles)
        {
            Assert.True(File.Exists(Path.Combine(testRoot, fileName)), $"{fileName} should hold a focused test domain.");
        }

        var programLineCount = File.ReadLines(programPath).Count();
        Assert.True(programLineCount <= 180, "Program.cs should stay as the executable harness entry point, not the test implementation container.");
        return Task.CompletedTask;
    }

    public static Task DocumentationIncludesWinUiFormatPlatform()
    {
        var readme = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "README.md"));
        var readmeZh = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "readme_zh.md"));
        var notes = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "DEVELOPMENT_NOTES.md"));

        Assert.Contains("$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore", readme, "README should document the WinUI platform needed for dotnet format.");
        Assert.Contains("$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore", readmeZh, "Chinese README should document the WinUI platform needed for dotnet format.");
        Assert.Contains("$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore", notes, "Development notes should document the WinUI platform needed for dotnet format.");
        return Task.CompletedTask;
    }

    public static Task AboutDialogGitHubLinkTargetsGuijianchouProfile()
    {
        var aboutPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "AboutDialog.xaml");

        var stringResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "StringResources.cs");

        var about = File.ReadAllText(aboutPath);
        var stringResources = File.ReadAllText(stringResourcesPath);
        var enResources = File.ReadAllText(Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw"));
        var zhResources = File.ReadAllText(Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "zh-cn",
            "Resources.resw"));

        Assert.Contains("NavigateUri=\"https://github.com/Guijianchou\"", about, "About dialog GitHub link should target the Guijianchou profile.");
        Assert.Contains(">@Guijianchou</Hyperlink>", about, "About dialog developer link should show the Guijianchou GitHub profile.");
        Assert.Contains("<value>GitHub Profile</value>", enResources, "About dialog GitHub label should match the profile target.");
        Assert.Contains("<value>GitHub 主页</value>", zhResources, "Chinese About dialog GitHub label should match the profile target.");
        Assert.Contains("StringResources.AboutGitHubProfile", about, "About dialog should bind through a profile-named resource property.");
        Assert.Contains("AboutGitHubProfile", stringResources, "String resource helper should expose a profile-named About label.");
        Assert.DoesNotContain("<value>GitHub Repository</value>", enResources, "About dialog GitHub label should not claim a repository when it opens a profile.");
        Assert.DoesNotContain("<value>GitHub 仓库</value>", zhResources, "Chinese About dialog GitHub label should not claim a repository when it opens a profile.");
        Assert.DoesNotContain("AboutRepository", about, "About dialog should not bind through a repository-named resource for a profile target.");
        Assert.DoesNotContain("AboutRepository", stringResources, "String resource helper should not expose a repository-named About profile label.");
        Assert.DoesNotContain("AboutRepository", enResources, "English resources should not keep the old repository-named About key.");
        Assert.DoesNotContain("AboutRepository", zhResources, "Chinese resources should not keep the old repository-named About key.");
        Assert.DoesNotContain("NavigateUri=\"https://github.com/guijianchou/OpenClaw_Manager\"", about, "About dialog repository link should no longer target the old OpenClaw Manager repository URL.");
        Assert.DoesNotContain("NavigateUri=\"https://github.com/Jutaosay/openclaw_for_windows\"", about, "About dialog should not link to the old repository.");
        Assert.DoesNotContain("https://github.com/Jutaosay", about, "About dialog should not link to the old developer profile.");
        return Task.CompletedTask;
    }

    public static Task SettingsWindowUsesNonBlockingFrameRefresh()
    {
        var themePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Theme.cs");
        var source = File.ReadAllText(themePath);

        Assert.DoesNotContain("redrawWindow: true", source, "Settings window should not force synchronous native redraws while opening.");
        Assert.DoesNotContain("repeatRefreshOnDarkTransition: true", source, "Settings window should not repeat dark-transition frame refreshes while opening.");
        Assert.DoesNotContain("rootElement.UpdateLayout()", source, "Settings window should not force a full layout pass during title-bar refresh.");
        return Task.CompletedTask;
    }

    public static Task SettingsWindowAvoidsFirstFrameBlackFlash()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var initializationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Initialization.cs");

        var xaml = File.ReadAllText(xamlPath);
        var initialization = File.ReadAllText(initializationPath);

        Assert.Contains("x:Name=\"SettingsRoot\"", xaml, "Settings root should be named so the first painted surface is explicit.");
        Assert.Contains("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", xaml, "Settings root should paint an opaque theme background before child content is ready.");
        Assert.DoesNotContain("MicaBackdrop", initialization, "Settings window should not rely on Mica during its first visible frame.");
        return Task.CompletedTask;
    }

    public static Task TitleBarCaptionButtonStatesUseOpaqueThemeColors()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowFrameHelper.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("FromArgb(96, 255, 255, 255)", source, "Caption button hover colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.DoesNotContain("FromArgb(144, 255, 255, 255)", source, "Caption button pressed colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.DoesNotContain("FromArgb(20, 0, 0, 0)", source, "Caption button hover colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.DoesNotContain("FromArgb(36, 0, 0, 0)", source, "Caption button pressed colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 55, 55, 55)", source, "Dark caption button hover should use a subtle opaque color.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 68, 68, 68)", source, "Dark caption button pressed should use a subtle opaque color.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 229, 229, 229)", source, "Light caption button hover should use a subtle opaque color.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 217, 217, 217)", source, "Light caption button pressed should use a subtle opaque color.");
        return Task.CompletedTask;
    }

    public static Task TopStatusPillLeavesRoomForLongModelNames()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var appXaml = ReadAppResourceXaml();

        Assert.Contains("x:Double x:Key=\"TopStatusPillMaxWidth\">780</x:Double>", appXaml, "Top status pill maximum width should preserve room for provider/model labels.");
        Assert.Contains("x:Double x:Key=\"TopStatusPillMinWidth\">440</x:Double>", appXaml, "Top status pill minimum width should preserve room before auth/status indicators.");
        Assert.Contains("MaxWidth=\"{StaticResource TopStatusPillMaxWidth}\"", xaml, "Top status pill should use the shared maximum width resource.");
        Assert.Contains("MinWidth=\"{StaticResource TopStatusPillMinWidth}\"", xaml, "Top status pill should use the shared minimum width resource.");
        Assert.Contains("x:Name=\"ModelStatusSegment\"", xaml, "Model status segment should be named so layout regressions are easy to test.");
        Assert.Contains("x:Double x:Key=\"TopStatusModelSegmentMinWidth\">190</x:Double>", appXaml, "Model status segment should reserve room for current OpenClaw provider/model names.");
        Assert.Contains("MinWidth=\"{StaticResource TopStatusModelSegmentMinWidth}\"", xaml, "Model status segment should use the shared minimum width resource.");
        Assert.Contains("x:Name=\"AccessStatusSegment\"", xaml, "Auth/access segment should be explicit in the status pill layout.");
        Assert.Contains("Thickness x:Key=\"TopStatusAccessSegmentMargin\">18,0,0,0</Thickness>", appXaml, "Auth/access segment margin should preserve a visible gap after long model labels.");
        Assert.Contains("Margin=\"{StaticResource TopStatusAccessSegmentMargin}\"", xaml, "Auth/access segment should use the shared margin resource.");
        return Task.CompletedTask;
    }

    public static Task TopStatusModelTextMatchesStatusBarFontSize()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var appXaml = ReadAppResourceXaml();
        var modelTextIndex = xaml.IndexOf("Text=\"{x:Bind ViewModel.ModelSummaryText, Mode=OneWay}\"", StringComparison.Ordinal);
        var statusTextIndex = xaml.IndexOf("x:Name=\"StatusText\"", StringComparison.Ordinal);

        Assert.True(modelTextIndex >= 0, "Model summary TextBlock should be present in the top status pill.");
        Assert.True(statusTextIndex >= 0, "Status bar text should be present for font-size comparison.");

        var modelTextBlock = xaml.Substring(modelTextIndex, Math.Min(260, xaml.Length - modelTextIndex));
        var statusTextBlock = xaml.Substring(statusTextIndex, Math.Min(260, xaml.Length - statusTextIndex));
        var statusBarStyle = ExtractStyleXaml(appXaml, "StatusBarTextBlockStyle");
        var modelValueStyle = ExtractStyleXaml(appXaml, "TopStatusModelValueTextBlockStyle");

        Assert.Contains("x:Double x:Key=\"StatusBarTextFontSize\">12</x:Double>", appXaml, "Status bar font size should be a shared app resource.");
        Assert.Contains("Value=\"{StaticResource StatusBarTextFontSize}\"", statusBarStyle, "Bottom status bar style should use the shared status-bar font-size resource.");
        Assert.Contains("Value=\"{StaticResource StatusBarTextFontSize}\"", modelValueStyle, "Top MODEL value style should use the same font-size resource as the status bar.");
        Assert.Contains("Style=\"{StaticResource TopStatusModelValueTextBlockStyle}\"", modelTextBlock, "Top MODEL value should use the shared model-value style.");
        Assert.Contains("Style=\"{StaticResource StatusBarTextBlockStyle}\"", statusTextBlock, "Status bar text should use the shared status-bar style.");
        return Task.CompletedTask;
    }

    public static Task TopStatusTypographyUsesSharedResources()
    {
        var mainWindowPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.xaml");

        var appXaml = ReadAppResourceXaml();
        var mainWindowXaml = File.ReadAllText(mainWindowPath);
        var topStatusPill = ExtractTopStatusPillXaml(mainWindowXaml);

        Assert.Contains("ResourceDictionary Source=\"Styles/StatusResources.xaml\"", appXaml, "App.xaml should merge top-status resources through a focused dictionary.");
        Assert.Contains("x:Double x:Key=\"TopStatusLabelFontSize\"", appXaml, "App resources should define top status label font size.");
        Assert.Contains("x:Double x:Key=\"TopStatusValueFontSize\"", appXaml, "App resources should define top status value font size.");
        Assert.Contains("x:Int32 x:Key=\"TopStatusLabelCharacterSpacing\"", appXaml, "App resources should define top status label character spacing.");
        Assert.Contains("x:Int32 x:Key=\"TopStatusValueCharacterSpacing\"", appXaml, "App resources should define top status value character spacing.");
        Assert.Contains("x:Key=\"TopStatusLabelTextBlockStyle\"", appXaml, "App resources should define a shared top status label style.");
        Assert.Contains("x:Key=\"TopStatusValueTextBlockStyle\"", appXaml, "App resources should define a shared top status value style.");
        Assert.Contains("x:Double x:Key=\"TopStatusPillMinWidth\">440</x:Double>", appXaml, "Top status minimum width should be a semantic resource.");
        Assert.Contains("x:Double x:Key=\"TopStatusPillMaxWidth\">780</x:Double>", appXaml, "Top status maximum width should be a semantic resource.");
        Assert.Contains("x:Double x:Key=\"TopStatusModelSegmentMinWidth\">190</x:Double>", appXaml, "Top status model segment width should be a semantic resource.");
        Assert.Contains("Style=\"{StaticResource TopStatusLabelTextBlockStyle}\"", topStatusPill, "Top status labels should use the shared label style.");
        Assert.Contains("Style=\"{StaticResource TopStatusValueTextBlockStyle}\"", topStatusPill, "Top status values should use the shared value style.");
        Assert.Contains("Style=\"{StaticResource TopStatusModelValueTextBlockStyle}\"", topStatusPill, "Top status model value should use the shared model-value style.");
        Assert.Contains("MinWidth=\"{StaticResource TopStatusPillMinWidth}\"", topStatusPill, "Top status pill should use the shared minimum width resource.");
        Assert.Contains("MaxWidth=\"{StaticResource TopStatusPillMaxWidth}\"", topStatusPill, "Top status pill should use the shared maximum width resource.");
        Assert.Contains("MinWidth=\"{StaticResource TopStatusModelSegmentMinWidth}\"", topStatusPill, "Model status segment should use the shared minimum width resource.");
        Assert.DoesNotContain("FontSize=\"10\"", topStatusPill, "Top status pill should not hard-code label/value font sizes.");
        Assert.DoesNotContain("FontSize=\"12\"", topStatusPill, "Top status pill should not hard-code the model value font size.");
        Assert.DoesNotContain("FontWeight=\"SemiBold\"", topStatusPill, "Top status pill should not repeat shared font weight inline.");
        return Task.CompletedTask;
    }

    public static Task SettingsWindowIsPrewarmedAfterStartup()
    {
        var commandsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Commands.cs");
        var lifecyclePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Lifecycle.cs");

        var commands = File.ReadAllText(commandsPath);
        var lifecycle = File.ReadAllText(lifecyclePath);

        Assert.Contains("PrewarmSettingsWindow", commands, "MainWindow should pre-create Settings after startup so the first click does not pay XAML construction cost.");
        Assert.Contains("DispatcherQueuePriority.Low", lifecycle, "Settings prewarm should run at low priority after the initial window load.");
        Assert.Contains("PrewarmSettingsWindow()", lifecycle, "MainWindow should schedule Settings prewarm from root load.");
        return Task.CompletedTask;
    }

    public static Task SettingsLanguageSelectionSyncsAfterLoad()
    {
        var initializationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Initialization.cs");
        var themePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Theme.cs");

        var initialization = File.ReadAllText(initializationPath);
        var theme = File.ReadAllText(themePath);
        var initializeNavigationIndex = initialization.IndexOf("private void InitializeNavigationState()", StringComparison.Ordinal);
        var rootLoadedIndex = theme.IndexOf("private void OnRootLoaded", StringComparison.Ordinal);
        var rootLoadedSelectionIndex = theme.IndexOf("SetLanguageSelection(ViewModel.SelectedLanguage)", StringComparison.Ordinal);
        var syncIndex = theme.IndexOf("public void SyncWithCurrentSettings()", StringComparison.Ordinal);
        var syncSelectionIndex = theme.IndexOf("SetLanguageSelection(ViewModel.SelectedLanguage)", syncIndex, StringComparison.Ordinal);

        Assert.True(initializeNavigationIndex >= 0, "Settings navigation initialization should exist.");
        Assert.DoesNotContain("SetLanguageSelection(ViewModel.SelectedLanguage);", initialization, "Language selection should not be finalized before the Settings window is loaded.");
        Assert.True(rootLoadedIndex >= 0, "Settings root Loaded handler should exist.");
        Assert.True(rootLoadedSelectionIndex > rootLoadedIndex, "Settings root Loaded should re-apply language selection after ComboBox items are loaded.");
        Assert.True(syncIndex >= 0, "Settings should expose SyncWithCurrentSettings.");
        Assert.True(syncSelectionIndex > syncIndex, "Settings activation sync should re-apply language selection before showing the window.");
        return Task.CompletedTask;
    }

    public static Task SettingsPrewarmIsRequeuedAfterClose()
    {
        var commandsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Commands.cs");
        var commands = File.ReadAllText(commandsPath);
        var closedIndex = commands.IndexOf("private void OnSettingsWindowClosed", StringComparison.Ordinal);
        var requeueIndex = commands.IndexOf("QueueSettingsWindowPrewarm()", closedIndex, StringComparison.Ordinal);

        Assert.Contains("private void QueueSettingsWindowPrewarm()", commands, "Settings prewarm should be centralized in one low-priority queue helper.");
        Assert.True(closedIndex >= 0, "Settings closed handler should exist.");
        Assert.True(requeueIndex > closedIndex, "Closing Settings should queue the next prewarmed Settings instance.");
        return Task.CompletedTask;
    }

    public static Task SettingsLanguageOptionsAreCodePopulated()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var initializationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Initialization.cs");
        var navigationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Navigation.cs");
        var sharedPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Shared.cs");

        var xaml = File.ReadAllText(xamlPath);
        var initialization = File.ReadAllText(initializationPath);
        var navigation = File.ReadAllText(navigationPath);
        var shared = File.ReadAllText(sharedPath);

        Assert.DoesNotContain("<ComboBoxItem", xaml, "Language ComboBox items should not rely on x:Bind content in prewarmed windows.");
        Assert.Contains("PopulateLanguageOptions();", initialization, "Settings should populate language options in code before navigation selection.");
        Assert.Contains("LanguageComboBox.Items.Clear()", navigation, "Language option population should rebuild the ComboBox items explicitly.");
        Assert.Contains("new ComboBoxItem", navigation, "Language options should use concrete ComboBoxItem content and tags.");
        Assert.Contains("_isSyncingLanguageSelection", shared, "Language selection sync should guard against prewarm/activation events mutating the ViewModel.");
        Assert.Contains("if (_isSyncingLanguageSelection)", navigation, "Language selection changed handler should ignore programmatic sync changes.");
        return Task.CompletedTask;
    }
}
