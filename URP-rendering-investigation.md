# Beat Saber 1.45.1 left-eye rendering investigation

Date: 2026-09-23. Target: Beat Saber 1.45.1_27839, Unity 6000.3.19f1.

The user reports a flat, slowed left-eye image on Bigscreen Beyond throughout
the game, including the main menu. The right eye appears normal. Camera2's
desktop overlay is the strongest source-supported explanation. Headset
confirmation is still required; this investigation did not reproduce the fault.

## Evidence

The public source tree was searched for camera creation, projection overrides,
stereo mode changes, shader globals, render callbacks, blits, and screen overlays.
The installed Camera2 DLL matched the retained local build by SHA-256 before
changes. Its configuration includes a full-screen first-person camera with
position smoothing 6.7 and rotation smoothing 5.3. This provides a concrete
explanation for an image that feels both flat and delayed if it reaches an eye.

Camera2 used a `ScreenSpaceOverlay` canvas containing its mono camera textures.
It disabled the game's smooth desktop camera and did not create a separate
desktop output camera. Spectator cameras already set `allowXRRendering = false`
and render to textures, so merely adding that flag to them would not fix this.

Inspection of the installed game assemblies established the following:

- `UniversalRenderPipeline.Render` sorts cameras and determines the last base
  camera. `UniversalRenderer.OnAfterRendering` schedules the non-HDR screen UI
  pass against that camera's backbuffer color and depth targets.
- `UniversalCameraData.rendersOverlayUI` uses `resolveToScreen`; this accepts
  game/VR cameras without a target texture and does not exclude XR.
- `DrawScreenSpaceUIPass.RenderOverlay` binds those targets and draws the overlay
  renderer list. The desktop canvas therefore needs explicit output ownership.
- The game's `BloomPrePassRendererSO` contains asymmetric stereo bloom offset
  handling. No public-source patch replacing that calculation was found.

These facts support desktop-image leakage into XR. They do not establish which
eye receives the overlay on this headset; that part comes from the user's
reported symptom, not a GPU capture.

The latest available game/Player logs are from an `fpfc` launch with an unavailable
OpenXR runtime. They confirm the game and Unity versions but cannot validate
headset rendering. No runtime configuration or driver changes were made.

## Change

On URP, Camera2 now creates a final desktop base camera with XR disabled, no scene
culling, no post-processing, no shadows, and no requested depth/color copies.
It clears its own desktop target. The Camera2 canvas is enabled only while this
camera renders, excluding headset cameras, HDR UI passes on other cameras, and
explicit spectator render requests. Disabling the viewport removes callbacks
and disables its output camera.

The two immediate `GL.Clear` calls used during scene switches and viewport edits
are skipped on URP. Previously they cleared the currently bound target without
selecting the desktop; the new desktop camera handles clearing every frame.
The existing Built-in path is retained. No game assembly, headset setting,
mod version, or other mod source was changed.

## Other candidates

| Candidate | Finding |
| --- | --- |
| Camera2 post-processing | Uses `Graphics.Blit` in `endCameraRendering`. Unity warns about this API with XR. This remains a separate migration concern; it was not rewritten speculatively as part of the overlay fix. |
| AssetBundleLoadingTools | Installed configuration selects single-pass instanced rendering, not forced multipass. No evidence justifies changing the user's rendering mode. |
| Vivify | Retains Built-in camera callbacks and an incomplete post-processing port. Its map rendering path does not explain a fault already present in the initial menu. |
| BeatLeader / ScoreSaber | Replay camera paths retain legacy camera APIs. BeatLeader also creates a replay desktop overlay. These are follow-up migration issues, not evidence of this initial-menu fault. |
| CameraUtils | Registers cameras and adjusts visibility masks; no per-eye projection replacement found. |
| SiraUtil / OpenXRUnderswingFix | FPFC/XR lifecycle and pose prediction code were inspected. No per-eye image compositing mechanism found that matches the reported flat desktop-like image. |
| Game bloom/fog | Official 1.45.1 notes explicitly list a Bigscreen Beyond stereo bloom/fog fix. Preserve it while isolating mod rendering. |

## Validation and next check

Release build against `D:\BSManager\BSInstances\1.45.1`: zero errors and zero
warnings. Automatic copying and archive generation were disabled for that build.
Only the DLL is eligible for deployment. No tests or game launch were performed.

The next manual check is the main menu on Beyond: both eyes should have normal
depth and immediate head motion while the desktop retains Camera2's smoothed
view. Then check one ordinary song, desktop resizing/scene switching, and replay
UI. If the left eye remains wrong, disable Camera2 for one comparison launch
before changing other mods; retain the new VR-session log for evidence.

Local evidence and build log:
`../../build-logs/Rendering/2026-09-23/`.

## External sources

- [Beat Saber official Steam announcements](https://steamcommunity.com/app/620980/allnews/):
  1.45.1 engine update and Bigscreen Beyond bloom/fog correction.
- [Unity: Blit in URP](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/customize/blit-overview.html):
  XR limitations of legacy blit APIs.
- [Unity: UniversalAdditionalCameraData](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.UniversalAdditionalCameraData.html):
  camera-specific XR rendering control.

The installed assembly implementations take precedence over general package
documentation for the conclusions about this particular game build.
