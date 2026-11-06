using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Camera2.Behaviours {
	class PositionableCam : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {
		private static readonly int MainTex = Shader.PropertyToID("_MainTex");

		public Cam2 cam { get; private set; }

		private GameObject camOrigin;
		private GameObject camPreview;
		private Material viewMaterial;

		private Material hoverMaterial;
		private Material normalMaterial;

		private MeshRenderer renderer;

		public void Awake() {
			Shader markerShader = Shader.Find("Custom/Glowing") ??
				Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(shader => shader.name == "Custom/Glowing");
			if(markerShader == null || !markerShader.isSupported)
				markerShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
			if(markerShader == null)
				throw new InvalidOperationException("Camera2 could not find a supported camera marker shader.");
			normalMaterial = CreateMarkerMaterial(markerShader, new Color(1f, 1f, 1f, 0.7f));
			hoverMaterial = CreateMarkerMaterial(markerShader, new Color(0.5f, 1f, 1f, 1f));

			DontDestroyOnLoad(gameObject);

			camOrigin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			camOrigin.transform.parent = transform;

			camOrigin.transform.localScale = new Vector3(0.08f, 0.08f, 0.2f);
			camOrigin.transform.localPosition = new Vector3(0, 0, -(camOrigin.transform.localScale.x * .3f));
			camOrigin.transform.localEulerAngles = new Vector3(90f, 0, 0);

			renderer = camOrigin.GetComponent<MeshRenderer>();
			renderer.sharedMaterial = normalMaterial;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;

			camPreview = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Destroy(camPreview.GetComponent<Collider>());
			camPreview.transform.parent = transform;

			viewMaterial = new Material(Plugin.Shader_VolumetricBlit);
			camPreview.GetComponent<MeshRenderer>().material = viewMaterial;
		}

		private static Material CreateMarkerMaterial(Shader shader, Color color) {
			var material = new Material(shader);
			if(material.HasProperty("_Color")) material.SetColor("_Color", color);
			if(material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
			return material;
		}

		public void SetSource(Cam2 cam) {
			this.cam = cam;

			viewMaterial.SetTexture(MainTex, cam.renderTexture);
			SetPreviewPositionAndSize();
		}

		public void SetPreviewPositionAndSize(bool small = true) {
			var size = small ? cam.settings.previewScreenSize : Math.Min(cam.settings.previewScreenSize * 2f, 4);

			if(cam.UCamera.aspect > 1f) {
				camPreview.transform.localScale = new Vector3(size, size / cam.UCamera.aspect, 0);
			} else {
				camPreview.transform.localScale = new Vector3(size * cam.UCamera.aspect, size, 0);
			}

			if(cam.settings.worldCamUnderScreen) {
				camOrigin.transform.localScale = new Vector3(0.08f, 0.08f, 0.2f);
			} else {
				camOrigin.transform.localScale = new Vector3(0.04f, 0.05f, 0.1f);
			}

			camPreview.transform.localPosition = new Vector3(
				0, 
				cam.settings.worldCamUnderScreen ? 0.15f + (camPreview.transform.localScale.y / 2f) : 0, 
				camOrigin.transform.localPosition.z / 2
			);
		}

		public void OnPointerClick(PointerEventData eventData) {
			if(!(eventData.currentInputModule is VRUIControls.VRInputModule))
				return;

			CamPositioner.BeingDragCamera(cam);
		}

		public void OnPointerEnter(PointerEventData eventData) {
			renderer.sharedMaterial = hoverMaterial;
		}

		public void OnPointerExit(PointerEventData eventData) {
			renderer.sharedMaterial = normalMaterial;
		}

		private void OnDestroy() {
			if(normalMaterial != null)
				Destroy(normalMaterial);
			if(hoverMaterial != null)
				Destroy(hoverMaterial);
			if(viewMaterial != null)
				Destroy(viewMaterial);
		}
	}
}
