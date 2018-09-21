using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.ComponentModel;


[ExecuteInEditMode]
public class NewBehaviourScript : MonoBehaviour {

	public BB bb = new BB(); // EGAD, THIS SHOULD BE A SINGLETON. ARGH.
	
	public Camera thecam;							// camera that is to move to each lens cell position and render
	public RenderTexture tarTemplate;				// I build a tex2Darray based on this texture. Otherwise not used.
	public int tarTexPixels = 256; // wow, hard unescapable crash with 'out of memory' error. Maybe am not disposing of textures or something.
	// So still using texture template, tarTemplate
//	public GameObject cameraStandin;				// This is a representation of the fly-camera, an aide for visulaizing.
	public GameObject viewingPersonsPosition;		// Used if the playback screen is set up to mimic havin a lens array in front of it.
	public Vector3 cameraSetback = Vector3.zero;	// one trick is to set the fly cameras back towards the viewers position.
	public float toeIn = 0.0f;

	public Camera skyboxCam;
	public Camera backgroundCam;
	public Camera midgroundCam;
	public Camera foregroundCam;

	public bool recording = false;

	public GameObject PlaybackScreen;				// The mosaic fly-image is painted onto this screen.
	public Camera PlaybackScreenCam;				// And observed by this camera, which renders to the final 4K screen.

	public float spacing = 1.0f;					// Lens array paramters. I use PlaybackScreen params to change size, so leave spacing = 1
	public int nWide = 7;
	public int nTall = 7;
	public bool pointyPartUp = false;

	public bool drawMyGizmos = false; // set true to always draw the camera array frustums

//	public float[] clipzones = new float[6];
//	public float[] zoneFOVdeltas = new float[5];
	public float bloomDistance = 10.0f;

	//	private float screenDPI;


	// Sooo sloppy, I maintain a lot of stuff as global. Makes for a few side-effect procs.
	private Vector3[] translations; // array of virtual camera positions, and hex cell positions.
	private Vector3 txurhc,txulhc,txlrhc,txllhc; // four corners of extents of the translations array

	private RenderTexture tar;						// Made into a tex2dArray when created.
	private CommandBuffer commandsAfter;			// Have camera render a view, then xfer to slice in above array via this commandBuffer

	private int nTot; // total number of viewpoints, eg cells

	private Vector3 cameraPositionZero;
	private float cameraSetbackDistance = 0.0f;

	private GameObject[] taggedToBloom;
	private Vector3[] bloomScales; 			// track starting scale of each GameObject tagged with 'bloom. Use w to track z too.'
	private float[] bloomSpots;				// and distance to camera at starting point.

	private GameObject canvasGO;

	// SWISS ARMY KNIFE class here. These following take messages from my control panels in the game
	public void ChangeScreenCamSize( float news ) {
		PlaybackScreenCam.orthographicSize = news;
		Debug.Log ("Size");
		Debug.Log (news);
	}
	public void ChangeScreenCamX( float news ) {
		Vector3 was = PlaybackScreen.transform.localPosition;
		was.x = news;
		PlaybackScreen.transform.localPosition = was;
		Debug.Log ("X");
		Debug.Log (news);
	}
	public void ChangeScreenCamY( float news ) {
		Vector3 was = PlaybackScreen.transform.localPosition;
		was.y = news;
		PlaybackScreen.transform.localPosition = was;
		Debug.Log ("Y");
		Debug.Log (news);
	}
	public void ChangeScreenCamA( float news ) {
		Vector3 was = PlaybackScreen.transform.eulerAngles;
		was.y = news;
		PlaybackScreen.transform.eulerAngles = was;
		Debug.Log ("A");
		Debug.Log (news);
	}
	public void ChangeScreenCamT( float news ) {
		Vector3 was = PlaybackScreen.transform.eulerAngles;
		was.z = news;
		PlaybackScreen.transform.eulerAngles = was;
		Debug.Log ("T");
		Debug.Log (news);
	}

	public void ChangeFlyCamZ( float news ) {
		cameraSetback.z = news;
		Debug.Log ("Z");
		Debug.Log (news);
	}
	public void ChangeToein( float news ) {
		toeIn = news;
	}
	public void ChangeSkyCamFOV (float news)
	{
		if (skyboxCam) {
			skyboxCam.fieldOfView = news;
		}
	}
	public void ChangeBGCamFOV (float news)
	{
		if (backgroundCam) {
			backgroundCam.fieldOfView = news;
		}
	}
	public void ChangeMGCamFOV (float news)
	{
		if (midgroundCam) {
			midgroundCam.fieldOfView = news;
		}
	}
	public void ChangeFGCamFOV (float news)
	{
		if ((foregroundCam) && (foregroundCam.enabled)) {
			foregroundCam.fieldOfView = news;
		} else {
			thecam.fieldOfView = news;
		}
		Debug.Log ("FGFOV");
		Debug.Log (news);
	}
	public void changeShaderK2(float news) 
	{
		MeshRenderer mer = PlaybackScreen.GetComponent<MeshRenderer> ();
		mer.sharedMaterial.SetFloat("_k2",news);
	}
	public void changeShaderK3( float news)
	{
		MeshRenderer mer = PlaybackScreen.GetComponent<MeshRenderer> ();
		mer.sharedMaterial.SetFloat("_k3",news);
	}
	public void changeShaderCentripital( float news )
	{
		MeshRenderer mer = PlaybackScreen.GetComponent<MeshRenderer> ();
		mer.sharedMaterial.SetFloat("_centripital",news);
		Debug.Log ("Shader Centripital");
		Debug.Log (news);
	}
	public void RecordButtonClick ()
	{
		recording = !recording;
		if (recording) {
			canvasGO.SetActive (false);
			Debug.Log ("recording");
			Time.captureFramerate = 12;

        // Create the folder
			// hard codeded /media/roberta/Elements/Downloads or /media/roberta/Seagate1/RecordedFromSlatherpi
      	// System.IO.Directory.CreateDirectory("Recording");
		} else {
			Debug.Log ("not recording");
		}
	}
	/** problematic right now. Put in when all worked out, maybe
	public void ChangeFlyCamFOV( float news ) {
		thecam.fieldOfView = news;
	}
	public void ChangeFlyCamD( float news ) {
		// change distance to camera, adjusting field of view to maintain object size.
		if (news == 0)
			return;
		Debug.Log("changeing setback via 'ChangeFlyCamD");
		float zat = cameraSetback.z;
		float fov = thecam.fieldOfView;
		float prod = fov * zat;
		thecam.fieldOfView = prod / news;
		cameraSetback.z = news;
	}
	**/

	// Commence building the 3D camera

	// First, build the tex2dArray
	void MakeTex2DArrayFromCameraTarget (int slices)
	{
		bool tic;
		if (tar != null) {
			tic = tar.IsCreated ();
			if (tic) {
				Debug.Log ("tar is already created. Release it");
				tar.Release (); // dono. Dispose or something?
			}
		}
		if (tarTemplate != null) {
			tar = new RenderTexture (tarTemplate); // fails on Ubuntu version. Umm. but not failing on 2017.3
		} else {
			tar = new RenderTexture(tarTexPixels,tarTexPixels,16,RenderTextureFormat.ARGB32);
		}
		tar.useMipMap = false;
		tar.dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray; 
		tar.volumeDepth = slices;
		tar.enableRandomWrite = false;
		tar.Create ();
		Shader.SetGlobalTexture ("_my2darray",tar);
	}

	int updateTranslations ()
	{
		int nT; // becomes nTot
		float spacingSign = 1.0f;
		if (!pointyPartUp)
			spacingSign = -1.0f;
		nT = bb.MakeTranslations (spacingSign * spacing, nWide, nTall, ref translations); 
		// negative spacing flags flatside up cell array.
		Mesh projectionMesh = new Mesh (); 
		bb.MakeHexMesh (translations, ref projectionMesh); 
		// takes its flat side info clue from the step between translations
		PlaybackScreen.GetComponent<MeshFilter> ().sharedMesh = projectionMesh;

		// now find four corners, for drawing the gizmo representation of the camera frustums
		// to show the extent of what the lens array is looking at
		Vector3 txmin = translations[0];
		Vector3 txmax = translations[nT-1];
		for (int i = 0; i < nT; i++) {
			txmin = Vector3.Min(txmin,translations[i]);
			txmax = Vector3.Max(txmax,translations[i]);
		}
		txulhc = new Vector3(txmin.x,txmax.y,0);
		txurhc = new Vector3(txmax.x,txmax.y,0);
		txllhc = new Vector3(txmin.x,txmin.y,0);
		txlrhc = new Vector3(txmax.x,txmin.y,0);
		Debug.Log("tx x");
		Debug.Log(txulhc.x);

		return nT;
	}

	void MakeCameraStandin() {
		// To draw a picture of the camera to visulaize the setup in the scene,
		// I need to create the mesh for it.
		Mesh wireMesh = new Mesh ();
		bb.MakeCameraIconHexMesh (translations, ref wireMesh);
		wireMesh.RecalculateNormals ();
	//	DestroyImmediate (cameraStandin.GetComponent<MeshFilter> ().sharedMesh); //.Clear(); // seems not to update the standin for the fly cam. 
	//	cameraStandin.GetComponent<MeshFilter> ().sharedMesh = wireMesh;
	}


	void OnDrawGizmos ()
	{
		// draw four corners of virtual camera array to show the field of views
		// I tried various DrawFrustum methods, none seem to be correct. Frustrating.
		Vector3 forwardNow = thecam.transform.forward;
		Vector3 lEu = thecam.transform.localEulerAngles; // all zeros as expected

		if  (drawMyGizmos) {
			Gizmos.color = Color.green;
			Gizmos.matrix = thecam.transform.parent.localToWorldMatrix;
		  	Gizmos.DrawLine(txulhc,txurhc);
			Gizmos.DrawLine(txllhc,txlrhc);
			Gizmos.DrawLine(txurhc,txlrhc);
			Gizmos.DrawLine(txulhc,txllhc);

			Gizmos.color = Color.red;

			Gizmos.DrawLine ( txulhc, txulhc + 20.0f*thecam.transform.forward);
			Gizmos.DrawLine ( txlrhc, txlrhc + 20.0f*thecam.transform.forward);
			Gizmos.DrawLine ( txllhc, txllhc + 20.0f*thecam.transform.forward);
			Gizmos.DrawLine ( txurhc, txurhc + 20.0f*thecam.transform.forward);

			Gizmos.color = Color.cyan;

			thecam.transform.localEulerAngles = new Vector3(txulhc.y,-txulhc.x,0f) * toeIn;
			Gizmos.DrawLine ( txulhc, txulhc + 20.0f*thecam.transform.forward);

		//	thecam.transform.localEulerAngles = 2.0f * txlrhc;
			thecam.transform.localEulerAngles = new Vector3(txlrhc.y,-txlrhc.x,0f) * toeIn;
			Gizmos.DrawLine ( txlrhc, txlrhc + 20.0f*thecam.transform.forward);

		//	thecam.transform.localEulerAngles = 2.0f * txllhc;
			thecam.transform.localEulerAngles = new Vector3(txllhc.y,-txllhc.x,0f) * toeIn;
			Gizmos.DrawLine ( txllhc, txllhc + 20.0f*thecam.transform.forward);

		//	thecam.transform.localEulerAngles = 2.0f * txurhc;
			thecam.transform.localEulerAngles = new Vector3(txurhc.y,-txurhc.x,0f) * toeIn;
			Gizmos.DrawLine ( txurhc, txurhc + 20.0f*thecam.transform.forward);
			thecam.transform.localEulerAngles = lEu;


			Gizmos.color = Color.yellow;
			Gizmos.DrawLine(viewingPersonsPosition.transform.position,txulhc);
			Gizmos.DrawLine(viewingPersonsPosition.transform.position,txlrhc);
			Gizmos.DrawLine(viewingPersonsPosition.transform.position,txllhc);
			Gizmos.DrawLine(viewingPersonsPosition.transform.position,txurhc);
			// thecam.transform = camOrg; // restore thecam to original position
		}
	}
	void Awake () // chicken and egg problem here, seems not to run if cams not got, but not got if not run..
	{				// not solved. Needs to run once without 'run in editor' 
	//	foregroundCam = GameObject.FindGameObjectWithTag("ForegroundFlyCam").GetComponent<Camera>();
	//	midgroundCam = GameObject.FindGameObjectWithTag("MidgroundFlyCam").GetComponent<Camera>();
	//	backgroundCam = GameObject.FindGameObjectWithTag("BackgroundFlyCam").GetComponent<Camera>();

		cameraSetbackDistance = Vector3.Distance(thecam.transform.parent.position+cameraSetback,cameraPositionZero);
	}

	void Start ()
	{
		Animator theAnimator;
		nTot = updateTranslations (); // side effect: reates the global translations array
		Debug.Log (nTot);
		MakeTex2DArrayFromCameraTarget (nTot);
	//	MakeCameraStandin ();
		commandsAfter = new CommandBuffer ();
		thecam.AddCommandBuffer (CameraEvent.AfterEverything, commandsAfter);

		thecam.enabled = false;
		cameraPositionZero = thecam.transform.position;
		cameraSetbackDistance = Vector3.Distance(thecam.transform.parent.position+cameraSetback,cameraPositionZero);

		taggedToBloom = GameObject.FindGameObjectsWithTag ("Bloom");
		bloomScales = new Vector3[taggedToBloom.Length];
		bloomSpots = new float[taggedToBloom.Length];
		for (int gob = 0; gob < taggedToBloom.Length; gob++) {
			bloomScales[gob] = taggedToBloom[gob].transform.GetChild(0).localScale;
			bloomSpots[gob] = Vector3.Distance(taggedToBloom[gob].transform.position,thecam.transform.position);
		}

		canvasGO = GameObject.Find ("Canvas");

	}

	void OnValidate () // what to do if params change, like number of clipzones
	{

	}

	void Update ()
	{
		// apply bloom to objects tagged for Bloom
		float dcam;
		for (int gob = 0; gob < taggedToBloom.Length; gob++) {
			dcam = Vector3.Distance (taggedToBloom [gob].transform.position, thecam.transform.position);
			taggedToBloom [gob].transform.GetChild (0).localScale = Vector3.one * (bloomSpots [gob] + bloomDistance) / (dcam + bloomDistance);
		}

		// There are issues with the tar texture not being available at times
		// when I try to re-create live.
	
		if (commandsAfter == null) { // dont know why sometimes not there
			Debug.Log ("had to reinstate commandsAfter");
			commandsAfter = new CommandBuffer ();
			thecam.RemoveCommandBuffers (CameraEvent.AfterEverything);
			thecam.AddCommandBuffer (CameraEvent.AfterEverything, commandsAfter);
		}
		if ((tar == null) | (!tar.IsCreated ())) {
			Debug.Log ("re creating tar");
			tar.Create ();
			Shader.SetGlobalTexture ("_my2darray", tar);
		}

		//	For each position in the lens array go there and take a picture
		// putting it all into tar, the array of viewws from each lenslet position.
		for (int i = 0; i < nTot; i++) {
			// put camera at position
			thecam.transform.localPosition = translations [i] - cameraSetback;
			// rather a hack, dependent on Eulers being zero to start, and translations in xy, and small angles. 
			thecam.transform.localEulerAngles = new Vector3 (translations [i].y, -translations [i].x, 0f) * toeIn;

			//	thecam.transform.LookAt ( thecam.transform.position +( viewingPersonsPosition.transform.position-translations[i]));
			// thecam.transform.LookAt (2.0f * thecam.transform.position - viewingPersonsPosition.transform.position);
			// also tilt it to be looking in the direction of the viewingPersonsPosition
			// or, parrallel to direction from viewingPersonsPosition to lens cell position.
			// that is, set lookAt as if looking at the camera from the position of the viewingPerson

			// now find the slice index if using sliceShader
			int sliceN = Mathf.RoundToInt( 55.0f * (translations[i].x - txulhc.x ) /(txurhc.x - txulhc.x)) ;
			Shader.SetGlobalInt("_sliceNumber",sliceN);


			commandsAfter.Clear ();

			if (skyboxCam && skyboxCam.enabled) {
				thecam.clearFlags = CameraClearFlags.Skybox;
				thecam.fieldOfView = skyboxCam.fieldOfView;
				thecam.nearClipPlane = backgroundCam.farClipPlane + 100.0f;
				thecam.farClipPlane = backgroundCam.farClipPlane + 100.1f;
				thecam.depth = skyboxCam.depth;
				thecam.Render ();
			} else {
				thecam.clearFlags = CameraClearFlags.Color;
			}

			if (backgroundCam && backgroundCam.enabled) {
				thecam.fieldOfView = backgroundCam.fieldOfView;
				thecam.farClipPlane = backgroundCam.farClipPlane;
				thecam.nearClipPlane = backgroundCam.nearClipPlane;
				thecam.depth = backgroundCam.depth;
				thecam.Render ();
				thecam.clearFlags = CameraClearFlags.Nothing;
			}

			if (midgroundCam && midgroundCam.enabled) {
				thecam.fieldOfView = midgroundCam.fieldOfView;
				thecam.farClipPlane = midgroundCam.farClipPlane + cameraSetbackDistance;
				thecam.nearClipPlane = midgroundCam.nearClipPlane + cameraSetbackDistance;
				thecam.depth = midgroundCam.depth;
				thecam.Render ();
				thecam.clearFlags = CameraClearFlags.Nothing;
			}

			commandsAfter.CopyTexture (BuiltinRenderTextureType.CameraTarget, 0, tar, i); 
			if (foregroundCam && foregroundCam.enabled) {
				thecam.fieldOfView = foregroundCam.fieldOfView;
				thecam.farClipPlane = foregroundCam.farClipPlane + cameraSetbackDistance;
				thecam.nearClipPlane = foregroundCam.nearClipPlane + cameraSetbackDistance;
				thecam.depth = foregroundCam.depth;
			}
			thecam.Render ();

		}

		// restore camera position, just 'cause.
		thecam.transform.localPosition = Vector3.zero;
		thecam.transform.LookAt (2.0f * thecam.transform.position - viewingPersonsPosition.transform.position);

		if (recording) {
			// Append filename to folder name (format is '0005 shot.png"')
			string name = string.Format("{0}/{1:D04}fly.png", "/media/roberta/Seagate1/RecordedFromSlatherpi", Time.frameCount);

        // Capture the screenshot to the specified file.
       		 ScreenCapture.CaptureScreenshot(name);
		}

		if (Input.GetKey (KeyCode.Escape)) {
			Application.Quit ();
		}
		if (Input.GetKey (KeyCode.Space)) {
			if (recording) {
				recording = false;
				canvasGO.SetActive (true);
			}
		}

		if (Time.frameCount % 50 == 0) {
			Debug.Log ("Time per Frame:");
			Debug.Log (Time.smoothDeltaTime);
		}

	}// end Update
}
