[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$BuildDirectory
)

$ErrorActionPreference = 'Stop'
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory)
$projectRoot = Join-Path $buildRoot 'PreviewShaderBuild'
if (Test-Path -LiteralPath $projectRoot) {
    throw "Use a new isolated build directory: $projectRoot"
}
$editorPath = (Resolve-Path -LiteralPath $UnityEditor).Path
$editorVersion = (Get-Item -LiteralPath $editorPath).VersionInfo.ProductVersion
if ($editorVersion -notlike '6000.3.19f1*') {
    throw "Preview bundle requires Unity 6000.3.19f1; found $editorVersion"
}
foreach ($directory in @('Assets/Editor', 'Packages', 'ProjectSettings', 'Output')) {
    [IO.Directory]::CreateDirectory((Join-Path $projectRoot $directory)) | Out-Null
}
# AssetBundle is a native class supplied by this built-in module, including in build output.
'{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}' |
    Set-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json')
'm_EditorVersion: 6000.3.19f1' |
    Set-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VolumetricBlit.shader') -Destination (Join-Path $projectRoot 'Assets/VolumetricBlit.shader')
@'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildPreview {
    public static void Build() {
        try {
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            typeof(PlayerSettings).GetProperty("virtualRealitySupported").SetValue(null, true);
            var graphicsSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            graphicsSettings.FindProperty("m_InstancingStripping").intValue = 0;
            graphicsSettings.ApplyModifiedPropertiesWithoutUndo();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/VolumetricBlit.shader");
            if (shader == null) throw new Exception("Preview shader is missing");
            var bundles = new[] { new AssetBundleBuild {
                assetBundleName = "camera2preview",
                assetNames = new[] { "Assets/VolumetricBlit.shader" }
            }};
            var result = BuildPipeline.BuildAssetBundles("Output", bundles,
                BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) {
                Debug.Log(message.severity + ": " + message.message);
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new Exception(message.message);
            }
            if (result == null || !File.Exists("Output/camera2preview"))
                throw new Exception("Preview bundle build failed");
            Debug.Log("Camera2 preview bundle built successfully");
            EditorApplication.Exit(0);
        } catch (Exception error) {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $projectRoot 'Assets/Editor/BuildPreview.cs')
$arguments = @('-batchmode', '-nographics', '-projectPath', $projectRoot,
    '-executeMethod', 'BuildPreview.Build', '-logFile', (Join-Path $buildRoot 'shader-build.log'))
$quotedArguments = $arguments | ForEach-Object { '"' + $_ + '"' }
$buildProcess = Start-Process -FilePath $editorPath -ArgumentList $quotedArguments -WindowStyle Hidden -PassThru
$buildProcess.WaitForExit()
if ($buildProcess.ExitCode -ne 0) { throw "Preview bundle build failed: $buildRoot/shader-build.log" }
Copy-Item -LiteralPath (Join-Path $projectRoot 'Output/camera2preview') -Destination (Join-Path $PSScriptRoot 'camera2preview')
Write-Output "Preview bundle built: $PSScriptRoot/camera2preview"
