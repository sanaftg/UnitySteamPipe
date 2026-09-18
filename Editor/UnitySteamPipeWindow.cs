#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ftg.UnitySteamPipe.Editor
{
public sealed class UnitySteamPipeWindow : EditorWindow
{
    private sealed class WindowsNotificationHandle
    {
        public object NotifyIcon;
        public double DisposeAt;
    }

    private enum SteamBuildPlatform
    {
        Windows,
        MacOS
    }

    private const string PrefPrefix = "Ftg.UnitySteamPipe.";

    private static readonly List<WindowsNotificationHandle>
        ActiveWindowsNotifications = new();

    private const string DefaultAppId = "";
    private const string DefaultDepotId = "";
    private const string DefaultExeName = "Game.exe";
    private const string DefaultMacAppName = "Game.app";

    private const string DefaultEntitlementsPath =
        "Packages/net.f-tg.unity-steampipe/Editor/Steam/Steam.entitlements";

    [SerializeField]
    private string appId = DefaultAppId;

    [SerializeField]
    private string depotId = DefaultDepotId;

    [SerializeField]
    private string steamLogin = "";

    [SerializeField]
    private string steamworksSdkPath = "";

    [SerializeField]
    private string buildPath = "";

    [SerializeField]
    private string executableName = DefaultExeName;

    [SerializeField]
    private string buildDescription = "";

    [SerializeField]
    private string setLiveBranch = "";

    [SerializeField]
    private bool developmentBuild = false;

    [SerializeField]
    private bool notifyWhenFinished = true;

    [SerializeField]
    private SteamBuildPlatform buildPlatform;

    [SerializeField]
    private string macDepotId = "";

    [SerializeField]
    private string macBuildPath = "";

    [SerializeField]
    private string macAppName = DefaultMacAppName;

    [SerializeField]
    private string signingIdentity = "";

    [SerializeField]
    private string notaryKeychainProfile = "";

    [SerializeField]
    private string entitlementsPath = DefaultEntitlementsPath;

    private Vector2 scroll;
    private string logText = "";
    private bool isBusy;

    private string ContentBuilderPath =>
        Path.Combine(
            steamworksSdkPath,
            "tools",
            "ContentBuilder");

    private bool IsMacBuild =>
        buildPlatform == SteamBuildPlatform.MacOS;

    private bool IsMacEditor =>
        Application.platform == RuntimePlatform.OSXEditor;

    private string SelectedDepotId =>
        IsMacBuild
            ? macDepotId
            : depotId;

    private string SelectedBuildPath =>
        IsMacBuild
            ? macBuildPath
            : buildPath;

    private string SelectedExecutableName =>
        IsMacBuild
            ? macAppName
            : executableName;

    private string BuiltPlayerPath =>
        Path.Combine(
            SelectedBuildPath ?? string.Empty,
            SelectedExecutableName ?? string.Empty);

    private string SteamCmdPath =>
        IsMacEditor
            ? Path.Combine(
                ContentBuilderPath,
                "builder_osx",
                "steamcmd.sh")
            : Path.Combine(
                ContentBuilderPath,
                "builder",
                "steamcmd.exe");

    private string ScriptsPath =>
        Path.Combine(
            ContentBuilderPath,
            "scripts");

    private string OutputPath =>
        Path.Combine(
            ContentBuilderPath,
            "output");

    private string AppVdfPath =>
        Path.Combine(
            ScriptsPath,
            $"app_build_{appId}.vdf");

    private string DepotVdfPath =>
        Path.Combine(
            ScriptsPath,
            $"depot_build_{SelectedDepotId}.vdf");

    [MenuItem("Tools/SteamPipe")]
    public static void Open()
    {
        var window =
            GetWindow<UnitySteamPipeWindow>("SteamPipe");

        window.minSize =
            new Vector2(560, 620);

        window.Show();
    }

    private void OnEnable()
    {
        buildPlatform =
            (SteamBuildPlatform)EditorPrefs.GetInt(
                PrefPrefix + "BuildPlatform",
                Application.platform == RuntimePlatform.OSXEditor
                    ? (int)SteamBuildPlatform.MacOS
                    : (int)SteamBuildPlatform.Windows);

        appId =
            EditorPrefs.GetString(
                PrefPrefix + "AppId",
                DefaultAppId);

        depotId =
            EditorPrefs.GetString(
                PrefPrefix + "DepotId",
                DefaultDepotId);

        macDepotId =
            EditorPrefs.GetString(
                PrefPrefix + "MacDepotId",
                "");

        steamLogin =
            EditorPrefs.GetString(
                PrefPrefix + "SteamLogin",
                "");

        steamworksSdkPath =
            EditorPrefs.GetString(
                PrefPrefix + "SdkPath",
                "");

        buildPath =
            EditorPrefs.GetString(
                PrefPrefix + "BuildPath",
                Path.GetFullPath(
                    Path.Combine(
                        Application.dataPath,
                        "..",
                        "Build",
                        "Steam")));

        macBuildPath =
            EditorPrefs.GetString(
                PrefPrefix + "MacBuildPath",
                Path.GetFullPath(
                    Path.Combine(
                        Application.dataPath,
                        "..",
                        "Build",
                        "SteamMac")));

        executableName =
            EditorPrefs.GetString(
                PrefPrefix + "ExeName",
                DefaultExeName);

        macAppName =
            EditorPrefs.GetString(
                PrefPrefix + "MacAppName",
                DefaultMacAppName);

        signingIdentity =
            EditorPrefs.GetString(
                PrefPrefix + "SigningIdentity",
                "");

        notaryKeychainProfile =
            EditorPrefs.GetString(
                PrefPrefix + "NotaryKeychainProfile",
                "");

        entitlementsPath =
            EditorPrefs.GetString(
                PrefPrefix + "EntitlementsPath",
                DefaultEntitlementsPath);

        setLiveBranch =
            EditorPrefs.GetString(
                PrefPrefix + "Branch",
                "");

        developmentBuild =
            EditorPrefs.GetBool(
                PrefPrefix + "DevelopmentBuild",
                false);

        notifyWhenFinished =
            EditorPrefs.GetBool(
                PrefPrefix + "NotifyWhenFinished",
                true);

        buildDescription = CreateDefaultBuildDescription();
    }

    private static string CreateDefaultBuildDescription()
    {
        string version = PlayerSettings.bundleVersion?.Trim();
        string versionLabel = string.IsNullOrEmpty(version)
            ? "Version Unknown"
            : version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? version
                : $"v{version}";

        return $"{versionLabel} Unity Build {DateTime.Now:yyyy-MM-dd HH:mm}";
    }

    private void OnDisable()
    {
        SavePrefs();
    }

    private void SavePrefs()
    {
        EditorPrefs.SetInt(
            PrefPrefix + "BuildPlatform",
            (int)buildPlatform);

        EditorPrefs.SetString(
            PrefPrefix + "AppId",
            appId);

        EditorPrefs.SetString(
            PrefPrefix + "DepotId",
            depotId);

        EditorPrefs.SetString(
            PrefPrefix + "MacDepotId",
            macDepotId);

        EditorPrefs.SetString(
            PrefPrefix + "SteamLogin",
            steamLogin);

        EditorPrefs.SetString(
            PrefPrefix + "SdkPath",
            steamworksSdkPath);

        EditorPrefs.SetString(
            PrefPrefix + "BuildPath",
            buildPath);

        EditorPrefs.SetString(
            PrefPrefix + "MacBuildPath",
            macBuildPath);

        EditorPrefs.SetString(
            PrefPrefix + "ExeName",
            executableName);

        EditorPrefs.SetString(
            PrefPrefix + "MacAppName",
            macAppName);

        EditorPrefs.SetString(
            PrefPrefix + "SigningIdentity",
            signingIdentity);

        EditorPrefs.SetString(
            PrefPrefix + "NotaryKeychainProfile",
            notaryKeychainProfile);

        EditorPrefs.SetString(
            PrefPrefix + "EntitlementsPath",
            entitlementsPath);

        EditorPrefs.SetString(
            PrefPrefix + "Branch",
            setLiveBranch);

        EditorPrefs.SetBool(
            PrefPrefix + "DevelopmentBuild",
            developmentBuild);

        EditorPrefs.SetBool(
            PrefPrefix + "NotifyWhenFinished",
            notifyWhenFinished);
    }

    private void OnGUI()
    {
        using var scrollView =
            new EditorGUILayout.ScrollViewScope(scroll);

        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField(
            "Unity SteamPipe",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            IsMacBuild
                ? "macOSビルド → Developer ID署名 → Apple公証 → SteamPipeアップロードを行います。\n" +
                  "証明書とNotary認証情報はmacOS Keychainを使用し、パスワードは保存しません。"
                : "Windowsビルド作成 → SteamPipe用VDF生成 → SteamCMDでアップロードします。\n" +
                  "Steamのパスワードは保存しません。",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(isBusy))
        {
            DrawSteamSettings();

            EditorGUILayout.Space(12);

            DrawBuildSettings();

            EditorGUILayout.Space(12);

            DrawValidation();

            EditorGUILayout.Space(12);

            DrawActions();
        }

        EditorGUILayout.Space(16);

        EditorGUILayout.LabelField(
            "Log",
            EditorStyles.boldLabel);

        logText =
            EditorGUILayout.TextArea(
                logText,
                GUILayout.MinHeight(180),
                GUILayout.ExpandHeight(true));

        scroll = scrollView.scrollPosition;
    }

    private void DrawSteamSettings()
    {
        EditorGUILayout.LabelField(
            "Steam",
            EditorStyles.boldLabel);

        appId =
            EditorGUILayout.TextField(
                "App ID",
                appId);

        if (IsMacBuild)
        {
            macDepotId =
                EditorGUILayout.TextField(
                    "macOS Depot ID",
                    macDepotId);
        }
        else
        {
            depotId =
                EditorGUILayout.TextField(
                    "Windows Depot ID",
                    depotId);
        }

        steamLogin =
            EditorGUILayout.TextField(
                "Steam Login",
                steamLogin);

        DrawFolderField(
            "Steamworks SDK",
            ref steamworksSdkPath,
            "Steamworks SDK の sdk フォルダを選択");

        setLiveBranch =
            EditorGUILayout.TextField(
                new GUIContent(
                    "Set Live Branch",
                    "空欄ならアップロードのみ。development 等を指定すると、その非DefaultブランチにSetLiveします。"),
                setLiveBranch);
    }

    private void DrawBuildSettings()
    {
        EditorGUILayout.LabelField(
            "Unity Build",
            EditorStyles.boldLabel);

        buildPlatform =
            (SteamBuildPlatform)EditorGUILayout.EnumPopup(
                "Platform",
                buildPlatform);

        if (IsMacBuild)
        {
            DrawFolderField(
                "Build Path",
                ref macBuildPath,
                "UnityのmacOS Steamビルド出力先");

            macAppName =
                EditorGUILayout.TextField(
                    "App Bundle",
                    macAppName);

            DrawMacSigningSettings();
        }
        else
        {
            DrawFolderField(
                "Build Path",
                ref buildPath,
                "UnityのWindows Steamビルド出力先");

            executableName =
                EditorGUILayout.TextField(
                    "Executable",
                    executableName);
        }

        buildDescription =
            EditorGUILayout.TextField(
                "Description",
                buildDescription);

        developmentBuild =
            EditorGUILayout.Toggle(
                "Development Build",
                developmentBuild);

        notifyWhenFinished =
            EditorGUILayout.Toggle(
                "Notify When Finished",
                notifyWhenFinished);
    }

    private void DrawMacSigningSettings()
    {
        EditorGUILayout.Space(6);

        EditorGUILayout.LabelField(
            "macOS Signing / Notarization",
            EditorStyles.boldLabel);

        signingIdentity =
            EditorGUILayout.TextField(
                new GUIContent(
                    "Signing Identity",
                    "Developer ID Application: 名前 (TEAMID) の完全な証明書名"),
                signingIdentity);

        notaryKeychainProfile =
            EditorGUILayout.TextField(
                new GUIContent(
                    "Notary Profile",
                    "xcrun notarytool store-credentialsでKeychainへ登録したProfile名"),
                notaryKeychainProfile);

        entitlementsPath =
            EditorGUILayout.TextField(
                "Entitlements",
                entitlementsPath);

        EditorGUILayout.HelpBox(
            "macOSでは内部のFramework / Bundle / dylibを先に署名し、" +
            "最後にApp本体へEntitlements付きで署名します。",
            MessageType.None);
    }

    private void DrawValidation()
    {
        EditorGUILayout.LabelField(
            "Validation",
            EditorStyles.boldLabel);

        DrawCheck(
            "App ID",
            !string.IsNullOrWhiteSpace(appId),
            appId);

        DrawCheck(
            IsMacBuild
                ? "macOS Depot ID"
                : "Windows Depot ID",
            !string.IsNullOrWhiteSpace(SelectedDepotId),
            SelectedDepotId);

        DrawCheck(
            "Steam Login",
            !string.IsNullOrWhiteSpace(steamLogin),
            string.IsNullOrWhiteSpace(steamLogin)
                ? "未設定"
                : steamLogin);

        bool sdkOk =
            Directory.Exists(steamworksSdkPath);

        DrawCheck(
            "Steamworks SDK",
            sdkOk,
            sdkOk
                ? steamworksSdkPath
                : "未設定 / 見つかりません");

        bool steamCmdOk =
            File.Exists(SteamCmdPath);

        DrawCheck(
            IsMacEditor
                ? "steamcmd.sh"
                : "steamcmd.exe",
            steamCmdOk,
            steamCmdOk
                ? SteamCmdPath
                : "見つかりません");

        bool playerExists =
            BuiltPlayerExists();

        DrawCheck(
            IsMacBuild
                ? "Build APP"
                : "Build EXE",
            playerExists,
            playerExists
                ? BuiltPlayerPath
                : "まだビルドされていません");

        if (IsMacBuild)
        {
            DrawCheck(
                "macOS Editor",
                IsMacEditor,
                IsMacEditor
                    ? "署名・公証可能"
                    : "署名・公証にはmacOSが必要です");

            DrawCheck(
                "Signing Identity",
                !string.IsNullOrWhiteSpace(signingIdentity),
                string.IsNullOrWhiteSpace(signingIdentity)
                    ? "未設定"
                    : signingIdentity);

            DrawCheck(
                "Notary Profile",
                !string.IsNullOrWhiteSpace(notaryKeychainProfile),
                string.IsNullOrWhiteSpace(notaryKeychainProfile)
                    ? "未設定"
                    : notaryKeychainProfile);

            string absoluteEntitlements =
                GetAbsoluteProjectPath(entitlementsPath);

            DrawCheck(
                "Entitlements",
                File.Exists(absoluteEntitlements),
                File.Exists(absoluteEntitlements)
                    ? absoluteEntitlements
                    : "見つかりません");
        }
    }

    private void DrawActions()
    {
        EditorGUILayout.LabelField(
            "Actions",
            EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(
                    "Build",
                    GUILayout.Height(34)))
            {
                ScheduleEditorAction(() =>
                {
                    ClearLog();
                    bool succeeded = BuildGame();
                    NotifyFinished("Unity Build", succeeded);
                });
            }

            if (GUILayout.Button(
                    "Generate VDF",
                    GUILayout.Height(34)))
            {
                ScheduleEditorAction(() => GenerateVdfs());
            }

            if (GUILayout.Button(
                    "Upload",
                    GUILayout.Height(34)))
            {
                ScheduleEditorAction(StartUpload);
            }
        }

        if (IsMacBuild)
        {
            EditorGUILayout.Space(6);

            if (GUILayout.Button(
                    "Sign & Notarize macOS Build",
                    GUILayout.Height(34)))
            {
                ScheduleEditorAction(StartMacSigningAndNotarization);
            }
        }

        EditorGUILayout.Space(6);

        string completeActionLabel =
            IsMacBuild
                ? "BUILD, SIGN, NOTARIZE & UPLOAD"
                : "BUILD & UPLOAD";

        if (GUILayout.Button(
                completeActionLabel,
                GUILayout.Height(44)))
        {
            ScheduleEditorAction(StartCompletePipeline);
        }

        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Build Folder"))
            {
                if (!string.IsNullOrWhiteSpace(
                        SelectedBuildPath))
                {
                    Directory.CreateDirectory(
                        SelectedBuildPath);

                    EditorUtility.RevealInFinder(
                        SelectedBuildPath);
                }
            }

            if (GUILayout.Button("Open VDF Folder"))
            {
                if (Directory.Exists(ScriptsPath))
                {
                    EditorUtility.RevealInFinder(
                        ScriptsPath);
                }
            }

            if (GUILayout.Button("Clear Log"))
            {
                ClearLog();
            }
        }
    }

    private void DrawFolderField(
        string label,
        ref string value,
        string title)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            value =
                EditorGUILayout.TextField(
                    label,
                    value);

            if (GUILayout.Button(
                    "...",
                    GUILayout.Width(32)))
            {
                string initial =
                    Directory.Exists(value)
                        ? value
                        : "";

                string selected =
                    EditorUtility.OpenFolderPanel(
                        title,
                        initial,
                        "");

                if (!string.IsNullOrEmpty(selected))
                {
                    value = selected;
                }
            }
        }
    }

    private void ScheduleEditorAction(Action action)
    {
        if (action == null)
            return;

        EditorApplication.delayCall += () =>
        {
            if (this == null)
                return;

            try
            {
                action();
            }
            catch (Exception exception)
            {
                AppendLog("Action failed: " + exception.Message);
                Debug.LogException(exception);
            }
        };
    }

    private static void DrawCheck(
        string label,
        bool success,
        string detail)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(
                success
                    ? "✓"
                    : "✗",
                GUILayout.Width(18));

            GUILayout.Label(
                label,
                GUILayout.Width(110));

            GUILayout.Label(detail ?? "");
        }
    }

    private bool BuildGame()
    {
        if (string.IsNullOrWhiteSpace(
                SelectedBuildPath))
        {
            AppendLog(
                "Build Path が空です.");

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                SelectedExecutableName))
        {
            AppendLog(
                IsMacBuild
                    ? "App Bundle が空です."
                    : "Executable が空です.");

            return false;
        }

        if (IsMacBuild &&
            !SelectedExecutableName.EndsWith(
                ".app",
                StringComparison.OrdinalIgnoreCase))
        {
            AppendLog(
                "macOS App Bundle名は .app で終わる必要があります.");

            return false;
        }

        string[] scenes =
            GetEnabledScenes();

        if (scenes.Length == 0)
        {
            AppendLog(
                "Build Settings に有効なSceneがありません.");

            return false;
        }

        Directory.CreateDirectory(
            SelectedBuildPath);

        AppendLog(
            IsMacBuild
                ? "Unity macOS build start"
                : "Unity Windows x64 build start");

        AppendLog(
            BuiltPlayerPath);

        BuildOptions options =
            BuildOptions.None;

        if (developmentBuild)
        {
            options |=
                BuildOptions.Development;
        }

        var buildPlayerOptions =
            new BuildPlayerOptions
            {
                scenes = scenes,

                locationPathName =
                    BuiltPlayerPath,

                target =
                    IsMacBuild
                        ? BuildTarget.StandaloneOSX
                        : BuildTarget.StandaloneWindows64,

                options = options
            };

        BuildReport report =
            BuildPipeline.BuildPlayer(
                buildPlayerOptions);

        if (report.summary.result !=
            BuildResult.Succeeded)
        {
            AppendLog(
                $"Build FAILED: {report.summary.result}");

            return false;
        }

        AppendLog(
            $"Build succeeded: " +
            $"{report.summary.totalSize / (1024f * 1024f):F1} MB");

        RemoveSteamAppIdTxt();

        AssetDatabase.Refresh();

        return true;
    }

    private static string[] GetEnabledScenes()
    {
        var list =
            new List<string>();

        foreach (
            EditorBuildSettingsScene scene
            in EditorBuildSettings.scenes)
        {
            if (scene.enabled &&
                !string.IsNullOrWhiteSpace(
                    scene.path))
            {
                list.Add(
                    scene.path);
            }
        }

        return list.ToArray();
    }

    private void RemoveSteamAppIdTxt()
    {
        string path =
            Path.Combine(
                SelectedBuildPath,
                "steam_appid.txt");

        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);

        AppendLog(
            "steam_appid.txt をDepot対象から削除しました.");
    }

    private bool GenerateVdfs()
    {
        if (!ValidateSteamPipeSettings(
                requireBuild: true))
        {
            return false;
        }

        Directory.CreateDirectory(
            ScriptsPath);

        Directory.CreateDirectory(
            OutputPath);

        string depotVdf =
$@"""DepotBuildConfig""
{{
    ""DepotID"" ""{EscapeVdf(SelectedDepotId)}""

    ""ContentRoot"" ""{EscapeVdfPath(SelectedBuildPath)}""

    ""FileMapping""
    {{
        ""LocalPath"" ""*""
        ""DepotPath"" "".""
        ""recursive"" ""1""
    }}

    ""FileExclusion"" ""steam_appid.txt""
}}";

        File.WriteAllText(
            DepotVdfPath,
            depotVdf,
            new UTF8Encoding(false));

        string setLiveLine =
            string.IsNullOrWhiteSpace(
                setLiveBranch)
                ? ""
                : $"\n    \"SetLive\" \"{EscapeVdf(setLiveBranch)}\"\n";

        string appVdf =
$@"""AppBuild""
{{
    ""AppID"" ""{EscapeVdf(appId)}""
    ""Desc"" ""{EscapeVdf(buildDescription)}""
    ""BuildOutput"" ""{EscapeVdfPath(OutputPath)}""
    ""ContentRoot"" ""{EscapeVdfPath(SelectedBuildPath)}""{setLiveLine}
    ""Depots""
    {{
        ""{EscapeVdf(SelectedDepotId)}"" ""{EscapeVdfPath(DepotVdfPath)}""
    }}
}}";

        File.WriteAllText(
            AppVdfPath,
            appVdf,
            new UTF8Encoding(false));

        AppendLog(
            $"Depot VDF: {DepotVdfPath}");

        AppendLog(
            $"App VDF:   {AppVdfPath}");

        return true;
    }

    private async void StartUpload()
    {
        if (isBusy)
        {
            return;
        }

        if (!GenerateVdfs())
        {
            return;
        }

        isBusy = true;

        Repaint();

        bool succeeded = false;

        try
        {
            SavePrefs();

            succeeded = await UploadAsync();
        }
        catch (Exception ex)
        {
            AppendLog(
                "SteamPipe upload failed: " +
                ex.Message);
        }
        finally
        {
            isBusy = false;

            Repaint();
            NotifyFinished("SteamPipe Upload", succeeded);
        }
    }

    private async void StartMacSigningAndNotarization()
    {
        if (isBusy)
        {
            return;
        }

        ClearLog();

        if (!ValidateMacSigningSettings())
        {
            return;
        }

        isBusy = true;

        Repaint();

        bool succeeded = false;

        try
        {
            SavePrefs();

            succeeded = await SignAndNotarizeMacBuildAsync();
        }
        catch (Exception ex)
        {
            AppendLog(
                "macOS signing/notarization failed: " +
                ex.Message);
        }
        finally
        {
            isBusy = false;

            Repaint();
            NotifyFinished("macOS Signing / Notarization", succeeded);
        }
    }

    private async void StartCompletePipeline()
    {
        if (isBusy)
        {
            return;
        }

        ClearLog();

        isBusy = true;

        Repaint();

        bool succeeded = false;

        try
        {
            if (!BuildGame())
            {
                return;
            }

            if (IsMacBuild)
            {
                if (!ValidateMacSigningSettings())
                {
                    return;
                }

                if (!await SignAndNotarizeMacBuildAsync())
                {
                    return;
                }
            }

            if (!GenerateVdfs())
            {
                return;
            }

            SavePrefs();

            succeeded = await UploadAsync();
        }
        catch (Exception ex)
        {
            AppendLog(
                "Complete pipeline failed: " +
                ex.Message);
        }
        finally
        {
            isBusy = false;

            Repaint();
            NotifyFinished("SteamPipe Pipeline", succeeded);
        }
    }

    private async Task<bool> UploadAsync()
    {
        string arguments =
            $"+login {QuoteArgument(steamLogin)} " +
            $"+run_app_build {QuoteArgument(AppVdfPath)} " +
            "+quit";

        string fileName =
            SteamCmdPath;

        if (IsMacEditor)
        {
            fileName = "/bin/bash";

            arguments =
                $"{QuoteArgument(SteamCmdPath)} " +
                arguments;
        }

        AppendLog(
            "SteamCMD upload start...");

        int exitCode =
            await RunProcessAsync(
                fileName,
                arguments,
                Path.GetDirectoryName(
                    SteamCmdPath)
                ?? string.Empty);

        if (exitCode == 0)
        {
            AppendLog(
                "SteamPipe upload finished.");

            return true;
        }

        AppendLog(
            $"SteamCMD exited with code {exitCode}.");

        return false;
    }

    private async Task<bool> SignAndNotarizeMacBuildAsync()
    {
        string appPath =
            Path.GetFullPath(
                BuiltPlayerPath);

        string absoluteEntitlements =
            GetAbsoluteProjectPath(
                entitlementsPath);

        string archivePath =
            Path.Combine(
                Path.GetDirectoryName(appPath)
                ?? SelectedBuildPath,
                Path.GetFileNameWithoutExtension(
                    appPath)
                + ".notary.zip");

        AppendLog(
            "Developer ID signing start...");

        bool nestedResult =
            await SignNestedMacCodeAsync(
                appPath);

        if (!nestedResult)
        {
            AppendLog(
                "Nested code signing failed.");

            return false;
        }

        AppendLog(
            "Signing main app bundle...");

        int exitCode =
            await RunProcessAsync(
                "/usr/bin/codesign",
                "--force " +
                "--options runtime " +
                "--timestamp " +
                $"--entitlements {QuoteArgument(absoluteEntitlements)} " +
                $"--sign {QuoteArgument(signingIdentity)} " +
                $"{QuoteArgument(appPath)}",
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "Main app signing failed.");

            return false;
        }

        AppendLog(
            "codesign verification...");

        exitCode =
            await RunProcessAsync(
                "/usr/bin/codesign",
                "--verify " +
                "--deep " +
                "--strict " +
                "--verbose=2 " +
                $"{QuoteArgument(appPath)}",
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "codesign verification failed.");

            return false;
        }

        if (File.Exists(archivePath))
        {
            File.Delete(
                archivePath);
        }

        AppendLog(
            "Notarization archive creation...");

        exitCode =
            await RunProcessAsync(
                "/usr/bin/ditto",
                "-c -k --keepParent " +
                $"{QuoteArgument(appPath)} " +
                $"{QuoteArgument(archivePath)}",
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "Notarization archive creation failed.");

            return false;
        }

        AppendLog(
            "Apple notarization submit " +
            "(this can take several minutes)...");

        exitCode =
            await RunProcessAsync(
                "/usr/bin/xcrun",
                "notarytool submit " +
                QuoteArgument(archivePath) +
                " --keychain-profile " +
                QuoteArgument(
                    notaryKeychainProfile) +
                " --wait",
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "Apple notarization failed.");

            return false;
        }

        AppendLog(
            "Stapling notarization ticket...");

        exitCode =
            await RunProcessAsync(
                "/usr/bin/xcrun",
                "stapler staple " +
                QuoteArgument(appPath),
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "Stapling failed.");

            return false;
        }

        AppendLog(
            "Validating staple...");

        exitCode =
            await RunProcessAsync(
                "/usr/bin/xcrun",
                "stapler validate " +
                QuoteArgument(appPath),
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "Stapler validation failed.");

            return false;
        }

        AppendLog(
            "Gatekeeper assessment...");

        exitCode =
            await RunProcessAsync(
                "/usr/sbin/spctl",
                "--assess " +
                "--type execute " +
                "--verbose=2 " +
                QuoteArgument(appPath),
                SelectedBuildPath);

        if (exitCode != 0)
        {
            AppendLog(
                "Gatekeeper assessment failed.");

            return false;
        }

        if (File.Exists(archivePath))
        {
            File.Delete(
                archivePath);
        }

        AppendLog(
            "macOS signing and notarization finished.");

        return true;
    }

    private async Task<bool> SignNestedMacCodeAsync(
        string appPath)
    {
        List<string> paths =
            CollectNestedSignablePaths(
                appPath);

        AppendLog(
            $"Nested code objects: {paths.Count}");

        foreach (string path in paths)
        {
            string relativePath =
                GetRelativePathSafe(
                    appPath,
                    path);

            AppendLog(
                $"Signing nested code: {relativePath}");

            int exitCode =
                await RunProcessAsync(
                    "/usr/bin/codesign",
                    "--force " +
                    "--options runtime " +
                    "--timestamp " +
                    $"--sign {QuoteArgument(signingIdentity)} " +
                    $"{QuoteArgument(path)}",
                    SelectedBuildPath);

            if (exitCode != 0)
            {
                AppendLog(
                    $"Failed to sign: {relativePath}");

                return false;
            }
        }

        return true;
    }

    private static List<string> CollectNestedSignablePaths(string appPath)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        string contentsPath = Path.Combine(appPath, "Contents");

        if (!Directory.Exists(contentsPath))
            return result.ToList();

        // 1. .dylib / .so / 単一ファイルの .bundle
        foreach (string file in Directory.EnumerateFiles(
                     contentsPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);

            if (string.Equals(extension, ".dylib", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".so", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bundle", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(Path.GetFullPath(file));
            }
        }

        // 2. ディレクトリ型の .bundle / .framework / .xpc / nested .app
        foreach (string directory in Directory.EnumerateDirectories(
                     contentsPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(directory);

            if (string.Equals(extension, ".bundle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".framework", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xpc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".app", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(Path.GetFullPath(directory));
            }
        }

        // 内側から順番に署名
        return result
            .OrderByDescending(GetPathDepth)
            .ThenByDescending(p => Directory.Exists(p))
            .ToList();
    }

    private static void CollectNestedSignablePathsRecursive(
        string directory,
        string rootAppPath,
        HashSet<string> result)
    {
        IEnumerable<string> entries;

        try
        {
            entries =
                Directory.EnumerateFileSystemEntries(
                    directory);
        }
        catch
        {
            return;
        }

        foreach (string entry in entries)
        {
            FileAttributes attributes;

            try
            {
                attributes =
                    File.GetAttributes(entry);
            }
            catch
            {
                continue;
            }

            bool isDirectory =
                (attributes &
                 FileAttributes.Directory)
                != 0;

            bool isSymlink =
                (attributes &
                 FileAttributes.ReparsePoint)
                != 0;

            if (isSymlink)
            {
                continue;
            }

            if (isDirectory)
            {
                string name =
                    Path.GetFileName(entry);

                if (string.Equals(
                        name,
                        "_CodeSignature",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CollectNestedSignablePathsRecursive(
                    entry,
                    rootAppPath,
                    result);

                if (!string.Equals(
                        Path.GetFullPath(entry),
                        Path.GetFullPath(rootAppPath),
                        StringComparison.Ordinal))
                {
                    string extension =
                        Path.GetExtension(entry);

                    if (IsSignableBundleExtension(
                            extension))
                    {
                        result.Add(
                            Path.GetFullPath(entry));
                    }
                }
            }
            else
            {
                string extension =
                    Path.GetExtension(entry);

                if (IsSignableBinaryExtension(
                        extension))
                {
                    result.Add(
                        Path.GetFullPath(entry));
                }
            }
        }
    }

    private static bool IsSignableBundleExtension(
        string extension)
    {
        return
            string.Equals(
                extension,
                ".bundle",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                extension,
                ".framework",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                extension,
                ".xpc",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                extension,
                ".app",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSignableBinaryExtension(
        string extension)
    {
        return
            string.Equals(
                extension,
                ".dylib",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                extension,
                ".so",
                StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPathDepth(
        string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return 0;
        }

        int depth = 0;

        foreach (char c in path)
        {
            if (c == Path.DirectorySeparatorChar ||
                c == Path.AltDirectorySeparatorChar)
            {
                depth++;
            }
        }

        return depth;
    }

    private static string GetRelativePathSafe(
        string root,
        string path)
    {
        try
        {
            string rootFull =
                Path.GetFullPath(root)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string pathFull =
                Path.GetFullPath(path);

            if (pathFull.StartsWith(
                    rootFull,
                    StringComparison.Ordinal))
            {
                return pathFull
                    .Substring(rootFull.Length)
                    .TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
            }

            return pathFull;
        }
        catch
        {
            return path;
        }
    }

    private Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory)
    {
        var completion =
            new TaskCompletionSource<int>();

        var process =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo
                    {
                        FileName =
                            fileName,

                        Arguments =
                            arguments,

                        WorkingDirectory =
                            workingDirectory,

                        UseShellExecute =
                            false,

                        RedirectStandardOutput =
                            true,

                        RedirectStandardError =
                            true,

                        CreateNoWindow =
                            true
                    },

                EnableRaisingEvents =
                    true
            };

        process.OutputDataReceived +=
            (_, e) =>
            {
                if (string.IsNullOrEmpty(
                        e.Data))
                {
                    return;
                }

                EditorApplication.delayCall +=
                    () =>
                        AppendLog(
                            e.Data);
            };

        process.ErrorDataReceived +=
            (_, e) =>
            {
                if (string.IsNullOrEmpty(
                        e.Data))
                {
                    return;
                }

                EditorApplication.delayCall +=
                    () =>
                        AppendLog(
                            "[stderr] " +
                            e.Data);
            };

        process.Exited +=
            (_, _) =>
            {
                int exitCode =
                    process.ExitCode;

                EditorApplication.delayCall +=
                    () =>
                    {
                        process.Dispose();

                        completion.TrySetResult(
                            exitCode);
                    };
            };

        try
        {
            process.Start();

            process.BeginOutputReadLine();

            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            process.Dispose();

            completion.TrySetException(
                ex);
        }

        return completion.Task;
    }

    private bool ValidateSteamPipeSettings(
        bool requireBuild)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            AppendLog(
                "App ID が空です.");

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                SelectedDepotId))
        {
            AppendLog(
                "Depot ID が空です.");

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                steamLogin))
        {
            AppendLog(
                "Steam Login が空です.");

            return false;
        }

        if (!File.Exists(
                SteamCmdPath))
        {
            AppendLog(
                $"SteamCMDが見つかりません: {SteamCmdPath}");

            return false;
        }

        if (requireBuild &&
            !BuiltPlayerExists())
        {
            AppendLog(
                $"ビルド済みPlayerが見つかりません: {BuiltPlayerPath}");

            return false;
        }

        return true;
    }

    private bool ValidateMacSigningSettings()
    {
        if (!IsMacBuild)
        {
            AppendLog(
                "PlatformがmacOSではありません.");

            return false;
        }

        if (!IsMacEditor)
        {
            AppendLog(
                "署名とApple公証はmacOS Editor上で実行してください.");

            return false;
        }

        if (!BuiltPlayerExists())
        {
            AppendLog(
                $"macOS App Bundleが見つかりません: {BuiltPlayerPath}");

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                signingIdentity))
        {
            AppendLog(
                "Developer ID ApplicationのSigning Identityが空です.");

            return false;
        }

        if (string.IsNullOrWhiteSpace(
                notaryKeychainProfile))
        {
            AppendLog(
                "notarytoolのKeychain Profileが空です.");

            return false;
        }

        string absoluteEntitlements =
            GetAbsoluteProjectPath(
                entitlementsPath);

        if (!File.Exists(
                absoluteEntitlements))
        {
            AppendLog(
                $"Entitlementsが見つかりません: {absoluteEntitlements}");

            return false;
        }

        return true;
    }

    private bool BuiltPlayerExists()
    {
        return
            IsMacBuild
                ? Directory.Exists(
                    BuiltPlayerPath)
                : File.Exists(
                    BuiltPlayerPath);
    }

    private static string GetAbsoluteProjectPath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(
                path);
        }

        string projectRoot =
            Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    ".."));

        return Path.GetFullPath(
            Path.Combine(
                projectRoot,
                path));
    }

    private static string QuoteArgument(
        string value)
    {
        return "\"" +
               (value ?? string.Empty)
               .Replace(
                   "\"",
                   "\\\"") +
               "\"";
    }

    private static string EscapeVdf(
        string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value
            .Replace(
                "\\",
                "\\\\")
            .Replace(
                "\"",
                "\\\"");
    }

    private static string EscapeVdfPath(
        string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        return Path
            .GetFullPath(path)
            .Replace(
                "\\",
                "/");
    }

    private void ClearLog()
    {
        logText = "";

        Repaint();
    }

    private void AppendLog(
        string message)
    {
        string line =
            $"[{DateTime.Now:HH:mm:ss}] {message}";

        logText +=
            line +
            Environment.NewLine;

        Debug.Log(
            "[SteamPipe] " +
            message);

        Repaint();
    }

    private void NotifyFinished(
        string operation,
        bool succeeded)
    {
        if (!notifyWhenFinished)
        {
            return;
        }

        string result = succeeded
            ? "完了しました"
            : "失敗しました";

        ShowNotification(
            new GUIContent($"{operation} が{result}"),
            8d);

        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            TryShowWindowsNotification(
                operation,
                result,
                succeeded);
        }

        EditorApplication.Beep();
    }

    private static bool TryShowWindowsNotification(
        string operation,
        string result,
        bool succeeded)
    {
        try
        {
            Type notifyIconType = Type.GetType(
                "System.Windows.Forms.NotifyIcon, System.Windows.Forms");
            Type toolTipIconType = Type.GetType(
                "System.Windows.Forms.ToolTipIcon, System.Windows.Forms");
            Type systemIconsType = Type.GetType(
                "System.Drawing.SystemIcons, System.Drawing");

            if (notifyIconType == null || toolTipIconType == null ||
                systemIconsType == null)
            {
                return false;
            }

            object notifyIcon = Activator.CreateInstance(notifyIconType);
            object icon = systemIconsType.GetProperty(
                succeeded ? "Information" : "Error")?.GetValue(null);
            object balloonIcon = Enum.Parse(
                toolTipIconType,
                succeeded ? "Info" : "Error");

            notifyIconType.GetProperty("Icon")?.SetValue(notifyIcon, icon);
            notifyIconType.GetProperty("Text")?.SetValue(
                notifyIcon,
                "V4X SteamPipe");
            notifyIconType.GetProperty("BalloonTipTitle")?.SetValue(
                notifyIcon,
                operation);
            notifyIconType.GetProperty("BalloonTipText")?.SetValue(
                notifyIcon,
                $"{operation} が{result}");
            notifyIconType.GetProperty("BalloonTipIcon")?.SetValue(
                notifyIcon,
                balloonIcon);
            notifyIconType.GetProperty("Visible")?.SetValue(notifyIcon, true);
            notifyIconType.GetMethod(
                "ShowBalloonTip",
                new[] { typeof(int) })?.Invoke(
                notifyIcon,
                new object[] { 8000 });

            ActiveWindowsNotifications.Add(new WindowsNotificationHandle
            {
                NotifyIcon = notifyIcon,
                DisposeAt = EditorApplication.timeSinceStartup + 10d
            });
            EditorApplication.update -= CleanupWindowsNotifications;
            EditorApplication.update += CleanupWindowsNotifications;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[SteamPipe] Windows notification could not be displayed: " +
                exception.Message);
            return false;
        }
    }

    private static void CleanupWindowsNotifications()
    {
        double now = EditorApplication.timeSinceStartup;

        for (int index = ActiveWindowsNotifications.Count - 1;
             index >= 0;
             index--)
        {
            WindowsNotificationHandle handle =
                ActiveWindowsNotifications[index];

            if (handle == null || now < handle.DisposeAt)
                continue;

            if (handle.NotifyIcon != null)
            {
                Type notifyIconType = handle.NotifyIcon.GetType();
                notifyIconType.GetProperty("Visible")?.SetValue(
                    handle.NotifyIcon,
                    false);
                (handle.NotifyIcon as IDisposable)?.Dispose();
            }

            ActiveWindowsNotifications.RemoveAt(index);
        }

        if (ActiveWindowsNotifications.Count == 0)
            EditorApplication.update -= CleanupWindowsNotifications;
    }
}
}

#endif
