using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class FireSafetyManager : MonoBehaviour
{
    private enum SimState { Question_PPE, Question_Extinguisher, Question_Aim, Minigame_Extinguishing, Evacuation_Exit, Success }
    private SimState currentState;

    // 3D Environment Roots
    private GameObject roomRoot;
    private GameObject circuitBoardRoot;
    private GameObject fireRoot;
    private GameObject extinguisherRoot;

    // 3D Interactive Exit Doors
    private GameObject damagedDoorObj;
    private GameObject safeDoorObj;

    // Particle Systems
    private ParticleSystem boardFlamesPS;
    private ParticleSystem floorFlamesPS;
    private ParticleSystem electricalCorePS;
    private ParticleSystem heavyFloorSmokePS;
    private ParticleSystem boardSmokePS;
    private ParticleSystem wallSparksPS;
    private ParticleSystem moltenDripsPS;
    private ParticleSystem doorSmokeLeakPS;
    private ParticleSystem co2SprayPS;

    // Cached Emission Modules (prevents CS1612 compiler error)
    private ParticleSystem.EmissionModule boardFlamesEmission;
    private ParticleSystem.EmissionModule floorFlamesEmission;
    private ParticleSystem.EmissionModule electricalCoreEmission;
    private ParticleSystem.EmissionModule heavyFloorSmokeEmission;
    private ParticleSystem.EmissionModule boardSmokeEmission;
    private ParticleSystem.EmissionModule wallSparksEmission;
    private ParticleSystem.EmissionModule moltenDripsEmission;
    private ParticleSystem.EmissionModule doorSmokeLeakEmission;
    private ParticleSystem.EmissionModule co2SprayEmission;

    private Light fireLight;
    private Light sparkArcLight;
    private AudioSource alarmSource;
    private Camera activeCam; // Cached camera used to place the hand-held extinguisher
    private Draggable3DExtinguisher extinguisherDraggable;

    // Auto-Generated UI
    private Canvas uiCanvas;
    private Text bannerText;
    private Image bannerBg;
    private GameObject questionCard;
    private Text questionTitleText;
    private Text questionDescText;
    private Button[] optionButtons = new Button[3];
    private Text[] optionButtonTexts = new Text[3];
    private GameObject minigameHud;
    private Slider fireHealthBar;
    private GameObject successCard;

    // Extinguish Progress
    private float fireHealth = 1f;
    private float extinguishRate = 0.22f;
    private bool isExtinguishing = false;

    void Awake()
    {
        EnsureEventSystem();
    }

    void Start()
    {
        // Building the room/doors/fire here (rather than Awake) matters on
        // phone/AR builds: Awake can run before the device camera is fully
        // ready or tagged "MainCamera", which was silently placing the whole
        // room (including both exit doors) relative to a missing camera and
        // leaving them out of view. Start() runs after every object's Awake,
        // giving the camera time to be ready.
        EnsurePhysicsRaycaster();
        SetCameraSolidBackground();
        BuildRoomCircuitBoardAndDoors();
        BuildTallFlamesAndHeavySmoke();
        Build3DExtinguisher();
        BuildProceduralUI();
        BuildAlarmAudio();
        StartPPEQuestion();
    }

    void Update()
    {
        // Dynamic violent flame flicker
        if (fireLight != null && fireLight.enabled)
        {
            float noise = Mathf.PerlinNoise(Time.time * 10f, 0f);
            fireLight.intensity = 6.0f * (0.75f + noise * 0.5f);
        }

        // Crackling electric arc flashes at severed copper wires
        if (sparkArcLight != null && sparkArcLight.enabled)
        {
            sparkArcLight.intensity = (Random.value > 0.75f) ? Random.Range(2.5f, 5.0f) : 0f;
        }

        // Extinguishing progress
        if (currentState == SimState.Minigame_Extinguishing && isExtinguishing)
        {
            fireHealth -= extinguishRate * Time.deltaTime;
            fireHealth = Mathf.Clamp01(fireHealth);

            if (fireHealthBar != null) fireHealthBar.value = fireHealth;
            ApplyFireHealth(fireHealth);

            if (fireHealth <= 0f)
            {
                StartEvacuationStage();
            }
        }

        // Keep the extinguisher glued in front of the camera every frame while it's
        // in play - but only until the player grabs it. Positioning it only once
        // (at the moment the minigame starts) is what made it go missing on
        // phones: on many devices/AR setups Camera.main isn't guaranteed to be
        // settled a frame later, so a one-shot placement can land it off-frame
        // with nothing afterward to correct it. Once HasBeenGrabbed is true,
        // this must stop, or it fights the player's own drag input every frame.
        if (extinguisherRoot != null && extinguisherRoot.activeSelf
            && (extinguisherDraggable == null || !extinguisherDraggable.HasBeenGrabbed))
        {
            Camera cam = GetActiveCamera();
            if (cam != null)
            {
                extinguisherRoot.transform.position = cam.transform.position
                    + cam.transform.forward * 0.6f
                    - cam.transform.up * 0.18f
                    + cam.transform.right * 0.12f;
            }
        }
    }

    // Camera.main can be null or momentarily stale on some phone/AR setups
    // (untagged rig camera, camera swapped after session start, etc.). Fall back
    // to any active camera in the scene so the extinguisher never ends up
    // positioned relative to a null reference.
    private Camera GetActiveCamera()
    {
        if (activeCam == null || !activeCam.isActiveAndEnabled)
        {
            activeCam = Camera.main;
        }
        if (activeCam == null)
        {
            activeCam = FindObjectOfType<Camera>();
        }
        return activeCam;
    }

    // =========================================================================
    // 1. TRAINING QUESTION FLOW & EVACUATION LOGIC
    // =========================================================================
    private void StartPPEQuestion()
    {
        currentState = SimState.Question_PPE;
        isExtinguishing = false;
        fireHealth = 1f;
        ApplyFireHealth(1f);

        if (extinguisherRoot != null) extinguisherRoot.SetActive(false);
        if (successCard != null) successCard.SetActive(false);
        if (minigameHud != null) minigameHud.SetActive(false);
        if (questionCard != null) questionCard.SetActive(true);

        SetBanner("ELECTRICAL SHORT-CIRCUIT FIRE DETECTED!", new Color(0.9f, 0.15f, 0.15f));
        questionTitleText.text = "Step 1: Select Appropriate PPE";
        questionDescText.text = "Broken cables in the distribution board have ignited. What PPE must you equip before approaching?";

        SetOption(0, "Class 0 Insulated Rubber Gloves & Arc Flash Face Shield", () => {
            SetBanner("CORRECT: Insulated gloves & visor protect against high voltage & arc blast.", new Color(0.15f, 0.65f, 0.25f));
            Invoke(nameof(StartExtinguisherQuestion), 1.4f);
        });

        SetOption(1, "Standard Cotton Clothes & Leather Work Gloves", () => {
            SetBanner("DANGEROUS: Cotton ignites easily and leather provides zero electrical insulation!", new Color(0.8f, 0.2f, 0.2f));
        });

        SetOption(2, "No Gear - Run directly toward the fire with a bucket", () => {
            SetBanner("FATAL MISTAKE: Approaching live high-voltage fire without PPE leads to electrocution!", new Color(0.85f, 0.1f, 0.1f));
        });
    }

    private void StartExtinguisherQuestion()
    {
        currentState = SimState.Question_Extinguisher;
        SetBanner("STEP 2: SELECT EXTINGUISHER TYPE", new Color(0.9f, 0.55f, 0.1f));
        questionTitleText.text = "Step 2: Choose the Correct Extinguisher";
        questionDescText.text = "Which fire extinguisher is certified and safe for energized electrical equipment?";

        SetOption(0, "CO2 (Carbon Dioxide) / Dry Chemical Extinguisher", () => {
            SetBanner("CORRECT: Non-conductive CO2 gas starves the fire without electrocution risk.", new Color(0.15f, 0.65f, 0.25f));
            Invoke(nameof(StartAimQuestion), 1.4f);
        });

        SetOption(1, "Pressurized Water Jet Extinguisher (Class A)", () => {
            SetBanner("LETHAL ERROR: Water conducts electricity straight back to the operator!", new Color(0.85f, 0.1f, 0.1f));
        });

        SetOption(2, "AFFF Foam Spray Extinguisher", () => {
            SetBanner("INCORRECT: Foam contains water and creates an electrocution hazard on live circuits!", new Color(0.8f, 0.2f, 0.2f));
        });
    }

    private void StartAimQuestion()
    {
        currentState = SimState.Question_Aim;
        SetBanner("STEP 3: EXTINGUISHER AIM TECHNIQUE (P.A.S.S.)", new Color(0.9f, 0.55f, 0.1f));
        questionTitleText.text = "Step 3: Pointing the Nozzle";
        questionDescText.text = "According to the P.A.S.S. protocol, where must you direct the extinguisher horn?";

        SetOption(0, "Aim at the base of the fire & sweep side-to-side", () => {
            SetBanner("PERFECT: Aiming at the base smothers the burning fuel source directly!", new Color(0.15f, 0.65f, 0.25f));
            Invoke(nameof(StartMinigame), 1.2f);
        });

        SetOption(1, "Aim high directly at the top of the flames", () => {
            SetBanner("INEFFECTIVE: Gas disperses into the air without smothering the burning fuel.", new Color(0.8f, 0.2f, 0.2f));
        });

        SetOption(2, "Aim into the smoke column above the fire", () => {
            SetBanner("WRONG: Extinguishing smoke does not put out the burning cables beneath.", new Color(0.8f, 0.2f, 0.2f));
        });
    }

    private void StartMinigame()
    {
        currentState = SimState.Minigame_Extinguishing;
        isExtinguishing = true;
        if (questionCard != null) questionCard.SetActive(false);
        if (minigameHud != null) minigameHud.SetActive(true);

        SetBanner("DRAG EXTINGUISHER OVER THE BURNING CABLES TO PUT OUT FIRE!", new Color(0.1f, 0.5f, 0.85f));

        if (extinguisherRoot != null)
        {
            extinguisherRoot.SetActive(true);
            if (extinguisherDraggable != null) extinguisherDraggable.ResetGrabState();
            Camera cam = GetActiveCamera();
            if (cam != null)
            {
                extinguisherRoot.transform.position = cam.transform.position
                    + cam.transform.forward * 0.6f
                    - cam.transform.up * 0.18f
                    + cam.transform.right * 0.12f;
                extinguisherRoot.transform.rotation = cam.transform.rotation;
            }
        }

        if (co2SprayPS != null)
        {
            co2SprayEmission.rateOverTime = 70f;
            co2SprayPS.Play();
        }
    }

    // NEW: Evacuation Stage
    private void StartEvacuationStage()
    {
        currentState = SimState.Evacuation_Exit;
        isExtinguishing = false;

        if (co2SprayPS != null) co2SprayEmission.rateOverTime = 0f;
        if (extinguisherRoot != null) extinguisherRoot.SetActive(false);
        if (minigameHud != null) minigameHud.SetActive(false);

        SetBanner("FIRE EXTINGUISHED! HEAVY TOXIC SMOKE REMAINS: Click on the safe, undamaged exit door!", new Color(0.95f, 0.6f, 0.1f));
    }

    // Called when a door is clicked
    public void OnDoorClicked(bool isSafeDoor)
    {
        if (currentState != SimState.Evacuation_Exit) return;

        if (isSafeDoor)
        {
            SetBanner("SAFE EVACUATION: Exit opened! You evacuated the building safely.", new Color(0.12f, 0.7f, 0.3f));
            Invoke(nameof(CompleteSimulation), 1.2f);
        }
        else
        {
            SetBanner("DANGER! This door is hot and blocked by fire! Choose the undamaged exit!", new Color(0.9f, 0.15f, 0.15f));
        }
    }

    private void CompleteSimulation()
    {
        currentState = SimState.Success;
        if (successCard != null) successCard.SetActive(true);
    }

    public void RestartSimulation()
    {
        StartPPEQuestion();
    }

    private void SetOption(int index, string text, UnityEngine.Events.UnityAction onClickAction)
    {
        optionButtonTexts[index].text = text;
        optionButtons[index].onClick.RemoveAllListeners();
        optionButtons[index].onClick.AddListener(onClickAction);
    }

    private void SetBanner(string message, Color bgCol)
    {
        if (bannerText != null) bannerText.text = message;
        if (bannerBg != null) bannerBg.color = bgCol;
    }

    private void ApplyFireHealth(float norm)
    {
        boardFlamesEmission.rateOverTime = 170f * norm;
        floorFlamesEmission.rateOverTime = 220f * norm;
        electricalCoreEmission.rateOverTime = 120f * norm;
        heavyFloorSmokeEmission.rateOverTime = 95f * (norm > 0.01f ? (0.35f + norm * 0.65f) : 0f);
        boardSmokeEmission.rateOverTime = 55f * (norm > 0.01f ? (0.3f + norm * 0.7f) : 0f);
        wallSparksEmission.rateOverTime = 55f * (norm * norm);
        moltenDripsEmission.rateOverTime = 18f * (norm * norm);

        if (fireLight != null)
        {
            fireLight.intensity = 6.0f * norm;
            fireLight.enabled = norm > 0.02f;
        }

        if (sparkArcLight != null) sparkArcLight.enabled = norm > 0.05f;

        if (alarmSource != null)
        {
            alarmSource.volume = 0.55f * norm;
            if (norm <= 0.01f && alarmSource.isPlaying) alarmSource.Stop();
            else if (norm > 0.01f && !alarmSource.isPlaying) alarmSource.Play();
        }
    }

    // =========================================================================
    // 2. 3D ROOM, REALISTIC CIRCUIT BOARD & INTERACTIVE EXIT DOORS
    // =========================================================================
    private void BuildRoomCircuitBoardAndDoors()
    {
        roomRoot = new GameObject("3D_Room_Environment");
        roomRoot.transform.SetParent(transform, false);

        Shader solidShader = GetCompatibleSolidShader();
        Material floorMat = CreateSolidMaterial(solidShader, new Color(0.7f, 0.7f, 0.72f));
        floorMat.mainTexture = GenerateTileTexture(128);

        Material wallMat = CreateSolidMaterial(solidShader, new Color(0.82f, 0.82f, 0.80f));
        Material trimMat = CreateSolidMaterial(solidShader, new Color(0.22f, 0.22f, 0.24f));

        Vector3 center = Vector3.forward * 1.5f;
        Camera roomCam = GetActiveCamera();
        if (roomCam != null)
        {
            Vector3 camPos = roomCam.transform.position;
            Vector3 fwdFlat = Vector3.ProjectOnPlane(roomCam.transform.forward, Vector3.up).normalized;
            center = camPos + fwdFlat * 1.6f;
        }

        // Room Floor & Wall
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FloorSlab";
        floor.transform.SetParent(roomRoot.transform, false);
        floor.transform.position = center + new Vector3(0, -0.72f, 0);
        floor.transform.localScale = new Vector3(3.4f, 0.04f, 3.4f);
        floor.GetComponent<Renderer>().material = floorMat;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "BackWall";
        wall.transform.SetParent(roomRoot.transform, false);
        wall.transform.position = center + new Vector3(0, 0.46f, 1.5f);
        wall.transform.localScale = new Vector3(3.4f, 2.4f, 0.06f);
        wall.GetComponent<Renderer>().material = wallMat;

        GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trim.name = "Baseboard";
        trim.transform.SetParent(roomRoot.transform, false);
        trim.transform.position = center + new Vector3(0, -0.66f, 1.46f);
        trim.transform.localScale = new Vector3(3.4f, 0.08f, 0.03f);
        trim.GetComponent<Renderer>().material = trimMat;

        // REALISTIC CIRCUIT BOARD
        circuitBoardRoot = new GameObject("Realistic_Circuit_Board");
        circuitBoardRoot.transform.SetParent(roomRoot.transform, false);
        Vector3 boardPos = center + new Vector3(0, 0.25f, 1.44f);
        circuitBoardRoot.transform.position = boardPos;

        Material metalBoxMat = CreateSolidMaterial(solidShader, new Color(0.20f, 0.21f, 0.23f));
        Material interiorMat = CreateSolidMaterial(solidShader, new Color(0.12f, 0.12f, 0.14f));
        Material dinRailMat = CreateSolidMaterial(solidShader, new Color(0.72f, 0.72f, 0.75f));
        Material breakerMat = CreateSolidMaterial(solidShader, new Color(0.40f, 0.41f, 0.44f));
        Material charredMat = CreateSolidMaterial(solidShader, new Color(0.06f, 0.05f, 0.04f));
        Material redWireMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.15f, 0.1f));
        Material blueWireMat = CreateSolidMaterial(solidShader, new Color(0.15f, 0.4f, 0.9f));
        Material greenWireMat = CreateSolidMaterial(solidShader, new Color(0.15f, 0.75f, 0.2f));
        Material copperMat = CreateSolidMaterial(solidShader, new Color(0.96f, 0.62f, 0.25f));

        float w = 0.30f, h = 0.40f, d = 0.08f;

        // Cabinet Box & Door
        CreateSubCube(circuitBoardRoot.transform, new Vector3(0, 0, d * 0.5f), new Vector3(w, h, 0.01f), interiorMat);
        CreateSubCube(circuitBoardRoot.transform, new Vector3(-w * 0.5f, 0, 0), new Vector3(0.01f, h, d), metalBoxMat);
        CreateSubCube(circuitBoardRoot.transform, new Vector3(w * 0.5f, 0, 0), new Vector3(0.01f, h, d), metalBoxMat);
        CreateSubCube(circuitBoardRoot.transform, new Vector3(0, h * 0.5f, 0), new Vector3(w, 0.01f, d), metalBoxMat);
        CreateSubCube(circuitBoardRoot.transform, new Vector3(0, -h * 0.5f, 0), new Vector3(w, 0.01f, d), metalBoxMat);

        GameObject door = new GameObject("OpenCabinetDoor");
        door.transform.SetParent(circuitBoardRoot.transform, false);
        door.transform.localPosition = new Vector3(-w * 0.5f, 0, -d * 0.5f);
        door.transform.localRotation = Quaternion.Euler(0, -70f, 0);
        CreateSubCube(door.transform, new Vector3(w * 0.5f, 0, 0), new Vector3(w, h, 0.008f), metalBoxMat);

        // DIN Rail & Breakers
        CreateSubCube(circuitBoardRoot.transform, new Vector3(0, 0.07f, 0.025f), new Vector3(w - 0.04f, 0.02f, 0.006f), dinRailMat);
        for (int i = 0; i < 5; i++)
        {
            Vector3 mcbPos = new Vector3(-0.08f + i * 0.04f, 0.07f, 0.015f);
            CreateSubCube(circuitBoardRoot.transform, mcbPos, new Vector3(0.032f, 0.07f, 0.025f), (i >= 3) ? charredMat : breakerMat);
        }

        // Broken conduit with jagged frayed wires
        Vector3 breakOrigin = boardPos + new Vector3(0.08f, -0.16f, -0.02f);
        CreateSubCylinder(circuitBoardRoot.transform, new Vector3(0.08f, -0.18f, 0.01f), new Vector3(0.04f, 0.06f, 0.04f), charredMat, Vector3.zero);

        BuildWireSegment(circuitBoardRoot.transform, breakOrigin, breakOrigin + new Vector3(-0.02f, -0.09f, -0.04f), redWireMat, 0.009f);
        CreateSubCylinder(circuitBoardRoot.transform, breakOrigin + new Vector3(-0.02f, -0.10f, -0.04f), new Vector3(0.006f, 0.016f, 0.006f), copperMat, Vector3.zero);

        BuildWireSegment(circuitBoardRoot.transform, breakOrigin, breakOrigin + new Vector3(0.03f, -0.11f, -0.03f), blueWireMat, 0.009f);
        CreateSubCylinder(circuitBoardRoot.transform, breakOrigin + new Vector3(0.03f, -0.12f, -0.03f), new Vector3(0.006f, 0.016f, 0.006f), copperMat, Vector3.zero);

        BuildWireSegment(circuitBoardRoot.transform, breakOrigin, breakOrigin + new Vector3(0.01f, -0.13f, -0.01f), greenWireMat, 0.008f);
        CreateSubCylinder(circuitBoardRoot.transform, breakOrigin + new Vector3(0.01f, -0.14f, -0.01f), new Vector3(0.006f, 0.014f, 0.006f), copperMat, Vector3.zero);

        // Wall Scorch Blast Mark
        GameObject wallSoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wallSoot.transform.SetParent(circuitBoardRoot.transform, false);
        wallSoot.transform.position = boardPos + new Vector3(0.04f, -0.08f, -0.035f);
        wallSoot.transform.localScale = new Vector3(0.55f, 0.65f, 1f);
        Destroy(wallSoot.GetComponent<Collider>());
        wallSoot.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSootTexture(64) };

        Material pMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSoftCircleTexture(64) };

        // High-Voltage Electrical Arc Sparks
        wallSparksPS = CreateParticleChild("ArcSparks", breakOrigin + new Vector3(0, -0.1f, -0.03f), pMat, circuitBoardRoot.transform);
        var spMain = wallSparksPS.main;
        spMain.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.32f);
        spMain.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.0f);
        spMain.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
        spMain.gravityModifier = 0.95f;
        spMain.simulationSpace = ParticleSystemSimulationSpace.World;
        wallSparksEmission = wallSparksPS.emission;
        wallSparksEmission.rateOverTime = 55f;

        var spShape = wallSparksPS.shape;
        spShape.shapeType = ParticleSystemShapeType.Sphere;
        spShape.radius = 0.03f;

        var spCol = wallSparksPS.colorOverLifetime;
        spCol.enabled = true;
        Gradient spGrad = new Gradient();
        spGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(0.35f, 0.85f, 1f), 0f), new GradientColorKey(new Color(1f, 0.95f, 0.3f), 0.35f), new GradientColorKey(new Color(1f, 0.35f, 0f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        spCol.color = spGrad;

        // Molten burning plastic drops
        moltenDripsPS = CreateParticleChild("MoltenDrips", breakOrigin + new Vector3(0, -0.08f, -0.02f), pMat, circuitBoardRoot.transform);
        var drMain = moltenDripsPS.main;
        drMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        drMain.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        drMain.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.035f);
        drMain.gravityModifier = 1.4f;
        drMain.simulationSpace = ParticleSystemSimulationSpace.World;
        moltenDripsEmission = moltenDripsPS.emission;
        moltenDripsEmission.rateOverTime = 18f;

        var drCol = moltenDripsPS.colorOverLifetime;
        drCol.enabled = true;
        drCol.color = GetFlameGradient();

        // Spark Arc Flash Light
        GameObject sLight = new GameObject("SparkFlashLight");
        sLight.transform.SetParent(circuitBoardRoot.transform, false);
        sLight.transform.position = breakOrigin + new Vector3(0, -0.06f, -0.1f);
        sparkArcLight = sLight.AddComponent<Light>();
        sparkArcLight.type = LightType.Point;
        sparkArcLight.color = new Color(0.4f, 0.75f, 1f);
        sparkArcLight.range = 2.8f;
        sparkArcLight.intensity = 0f;

        // -------------------------------------------------------------
        // BUILD INTERACTIVE EXIT DOORS (1 DAMAGED, 1 SAFE)
        // -------------------------------------------------------------
        Material safeDoorMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.86f, 0.88f));
        Material burntDoorMat = CreateSolidMaterial(solidShader, new Color(0.12f, 0.10f, 0.09f));
        Material exitSignMat = CreateSolidMaterial(solidShader, new Color(0.15f, 0.85f, 0.25f)); // Glowing Green
        Material blockedSignMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.15f, 0.15f)); // Glowing Red

        // Door A: DAMAGED / BLOCKED EXIT (Left side)
        Vector3 damagedDoorPos = center + new Vector3(-1.05f, 0.18f, 1.45f);
        damagedDoorObj = BuildInteractiveDoor(damagedDoorPos, "DamagedDoor", burntDoorMat, blockedSignMat, false);

        // Smoke seeping through damaged door cracks
        doorSmokeLeakPS = CreateParticleChild("DoorSmokeLeak", damagedDoorPos + new Vector3(0, -0.7f, -0.03f), pMat, damagedDoorObj.transform);
        var dsmMain = doorSmokeLeakPS.main;
        dsmMain.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        dsmMain.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        dsmMain.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        doorSmokeLeakEmission = doorSmokeLeakPS.emission;
        doorSmokeLeakEmission.rateOverTime = 12f;

        // Door B: SAFE EMERGENCY EXIT (Right side)
        Vector3 safeDoorPos = center + new Vector3(1.05f, 0.18f, 1.45f);
        safeDoorObj = BuildInteractiveDoor(safeDoorPos, "SafeExitDoor", safeDoorMat, exitSignMat, true);
    }

    private GameObject BuildInteractiveDoor(Vector3 pos, string name, Material doorMat, Material signMat, bool isSafe)
    {
        GameObject doorObj = new GameObject(name);
        doorObj.transform.SetParent(roomRoot.transform, false);
        doorObj.transform.position = pos;

        if (isSafe)
        {
            // Door Slab (1.8m tall x 0.75m wide) - intact, undamaged
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "DoorLeaf";
            slab.transform.SetParent(doorObj.transform, false);
            slab.transform.localScale = new Vector3(0.75f, 1.75f, 0.04f);
            slab.GetComponent<Renderer>().material = doorMat;
            Destroy(slab.GetComponent<Collider>());

            // Door Handle
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.transform.SetParent(doorObj.transform, false);
            handle.transform.localPosition = new Vector3(0.28f, 0, -0.035f);
            handle.transform.localScale = new Vector3(0.03f, 0.12f, 0.04f);
            Destroy(handle.GetComponent<Collider>());
            handle.GetComponent<Renderer>().material = CreateSolidMaterial(GetCompatibleSolidShader(), new Color(0.6f, 0.6f, 0.62f));
        }
        else
        {
            // Genuinely shattered door - blown-open cavity, splintered planks, hanging
            // shards, and fallen debris at the foot, instead of one flat dark cube.
            BuildShatteredDoorLeaf(doorObj.transform, doorMat);
        }

        // Illuminated Sign Box above door
        GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.transform.SetParent(doorObj.transform, false);
        sign.transform.localPosition = new Vector3(0, 1.02f, -0.02f);
        sign.transform.localScale = new Vector3(0.45f, 0.14f, 0.05f);
        Destroy(sign.GetComponent<Collider>());
        sign.GetComponent<Renderer>().material = signMat;

        // Clickable Trigger Collider & Handler Component
        BoxCollider boxCol = doorObj.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(0.85f, 2.1f, 0.2f);
        var clicker = doorObj.AddComponent<DoorClickHandler>();
        clicker.Init(this, isSafe);

        return doorObj;
    }

    private void BuildShatteredDoorLeaf(Transform parent, Material charredMat)
    {
        Shader solidShader = GetCompatibleSolidShader();
        Material cavityMat = CreateSolidMaterial(solidShader, new Color(0.015f, 0.015f, 0.02f));
        Material splitCharMat = CreateSolidMaterial(solidShader, new Color(0.09f, 0.07f, 0.06f));
        Material emberEdgeMat = CreateSolidMaterial(solidShader, new Color(0.55f, 0.18f, 0.03f));

        // Dark blown-open cavity behind the frame, so the gaps between planks
        // read as an actual hole punched through the door rather than empty space.
        GameObject cavity = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cavity.name = "BlownOpenCavity";
        cavity.transform.SetParent(parent, false);
        cavity.transform.localPosition = new Vector3(0f, 0.05f, 0.01f);
        cavity.transform.localScale = new Vector3(0.68f, 1.62f, 0.012f);
        Destroy(cavity.GetComponent<Collider>());
        cavity.GetComponent<Renderer>().material = cavityMat;

        // Remaining frame boards - uneven widths, gaps, and tilts instead of a solid slab
        BuildDoorPlank(parent, new Vector3(-0.335f, 0.55f, -0.012f), new Vector3(0.095f, 0.62f, 0.036f), -5f, charredMat);
        BuildDoorPlank(parent, new Vector3(-0.235f, 0.10f, -0.018f), new Vector3(0.10f, 1.10f, 0.036f), 4f, splitCharMat);
        BuildDoorPlank(parent, new Vector3(0.305f, 0.60f, -0.012f), new Vector3(0.115f, 0.52f, 0.036f), 9f, charredMat);
        BuildDoorPlank(parent, new Vector3(0.24f, -0.42f, -0.02f), new Vector3(0.13f, 0.70f, 0.036f), -15f, splitCharMat);
        BuildDoorPlank(parent, new Vector3(-0.02f, -0.66f, -0.016f), new Vector3(0.17f, 0.34f, 0.036f), 11f, charredMat);

        // Header board - still mostly attached along the top, scorched and cracked
        BuildDoorPlank(parent, new Vector3(0f, 0.84f, -0.006f), new Vector3(0.72f, 0.13f, 0.038f), -2f, splitCharMat);

        // Jagged splinter shards jutting out at odd angles along the break
        BuildSplinterShard(parent, new Vector3(-0.06f, 0.28f, -0.01f), new Vector3(0.022f, 0.44f, 0.022f), 35f, emberEdgeMat);
        BuildSplinterShard(parent, new Vector3(0.08f, -0.04f, -0.015f), new Vector3(0.02f, 0.36f, 0.02f), -50f, emberEdgeMat);
        BuildSplinterShard(parent, new Vector3(-0.03f, -0.27f, -0.01f), new Vector3(0.022f, 0.28f, 0.022f), 62f, emberEdgeMat);

        // Broken-off chunks that have fallen and landed at the foot of the door
        BuildDebrisChunk(parent, new Vector3(-0.22f, -0.86f, 0.09f), new Vector3(0.22f, 0.045f, 0.16f), 18f, charredMat);
        BuildDebrisChunk(parent, new Vector3(0.16f, -0.865f, 0.15f), new Vector3(0.16f, 0.04f, 0.14f), -25f, splitCharMat);
        BuildDebrisChunk(parent, new Vector3(0.02f, -0.87f, 0.21f), new Vector3(0.12f, 0.035f, 0.10f), 42f, charredMat);
    }

    private void BuildDoorPlank(Transform parent, Vector3 localPos, Vector3 scale, float zRotDeg, Material mat)
    {
        GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plank.name = "BrokenPlank";
        plank.transform.SetParent(parent, false);
        plank.transform.localPosition = localPos;
        plank.transform.localRotation = Quaternion.Euler(0, 0, zRotDeg);
        plank.transform.localScale = scale;
        Destroy(plank.GetComponent<Collider>());
        plank.GetComponent<Renderer>().material = mat;
    }

    private void BuildSplinterShard(Transform parent, Vector3 localPos, Vector3 scale, float zRotDeg, Material mat)
    {
        GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shard.name = "SplinterShard";
        shard.transform.SetParent(parent, false);
        shard.transform.localPosition = localPos;
        shard.transform.localRotation = Quaternion.Euler(12f, -10f, zRotDeg);
        shard.transform.localScale = scale;
        Destroy(shard.GetComponent<Collider>());
        shard.GetComponent<Renderer>().material = mat;
    }

    private void BuildDebrisChunk(Transform parent, Vector3 localPos, Vector3 scale, float yRotDeg, Material mat)
    {
        GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chunk.name = "FallenDebris";
        chunk.transform.SetParent(parent, false);
        chunk.transform.localPosition = localPos;
        chunk.transform.localRotation = Quaternion.Euler(6f, yRotDeg, -4f);
        chunk.transform.localScale = scale;
        Destroy(chunk.GetComponent<Collider>());
        chunk.GetComponent<Renderer>().material = mat;
    }

    // =========================================================================
    // 3. TALL AGGRESSIVE ELECTRICAL FLAMES & A LOT OF BLACK SMOKE (NO WOOD)
    // =========================================================================
    private void BuildTallFlamesAndHeavySmoke()
    {
        fireRoot = new GameObject("Electrical_Fire_Blaze");
        fireRoot.transform.SetParent(transform, false);

        Vector3 groundCenter = Vector3.forward * 1.5f + new Vector3(0.08f, -0.68f, 0.65f);
        if (circuitBoardRoot != null)
        {
            groundCenter = new Vector3(circuitBoardRoot.transform.position.x + 0.08f, -0.68f, circuitBoardRoot.transform.position.z - 0.70f);
        }
        fireRoot.transform.position = groundCenter;

        Shader solidShader = GetCompatibleSolidShader();
        Material meltedGlowMat = CreateSolidMaterial(solidShader, new Color(0.95f, 0.28f, 0.04f));

        // Large Floor Electrical Scorch Burn Decal
        GameObject groundSoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        groundSoot.name = "FloorScorchMark";
        groundSoot.transform.SetParent(fireRoot.transform, false);
        groundSoot.transform.localPosition = new Vector3(0, 0.002f, 0);
        groundSoot.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        groundSoot.transform.localScale = new Vector3(1.8f, 1.6f, 1f);
        Destroy(groundSoot.GetComponent<Collider>());
        groundSoot.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSootTexture(64) };

        // Flat molten blister pool on the floor (NO CYLINDERS / NO WOOD STICKS)
        CreateSubCube(fireRoot.transform, new Vector3(0, 0.008f, 0), new Vector3(0.35f, 0.015f, 0.30f), meltedGlowMat);

        Material pMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSoftCircleTexture(64) };

        // --- LAYER 1: TALL LONG FLAMES (RAGING UPWARD FROM FLOOR) ---
        floorFlamesPS = CreateParticleChild("TallFloorFlames", groundCenter + new Vector3(0, 0.05f, 0), pMat, fireRoot.transform);
        var ffMain = floorFlamesPS.main;
        ffMain.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f); // Longer-lived so flames climb much higher
        ffMain.startSpeed = new ParticleSystem.MinMaxCurve(3.4f, 6.2f); // Huge upward velocity for TALL flames
        ffMain.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.7f); // Wide, huge flame tongues
        ffMain.simulationSpace = ParticleSystemSimulationSpace.World;
        floorFlamesEmission = floorFlamesPS.emission;
        floorFlamesEmission.rateOverTime = 220f; // Dense, roaring blaze

        var ffShape = floorFlamesPS.shape;
        ffShape.shapeType = ParticleSystemShapeType.Circle;
        ffShape.radius = 0.42f; // Wider base for a huge fire
        ffShape.rotation = new Vector3(-90f, 0, 0);

        var ffCol = floorFlamesPS.colorOverLifetime;
        ffCol.enabled = true;
        ffCol.color = GetFlameGradient();

        var ffSize = floorFlamesPS.sizeOverLifetime;
        ffSize.enabled = true;
        AnimationCurve fCurve = new AnimationCurve();
        fCurve.AddKey(0f, 0.35f);
        fCurve.AddKey(0.25f, 1.0f);
        fCurve.AddKey(0.75f, 0.55f);
        fCurve.AddKey(1f, 0.04f); // Tapers into long licking tongues
        ffSize.size = new ParticleSystem.MinMaxCurve(1f, fCurve);

        var ffNoise = floorFlamesPS.noise;
        ffNoise.enabled = true;
        ffNoise.strength = 0.55f;
        ffNoise.frequency = 0.65f;
        ffNoise.scrollSpeed = 1.8f;

        // --- LAYER 2: CIRCUIT BOARD FLAMES (BURSTING OUT OF BROKEN WIRES) ---
        Vector3 boardFlamesPos = (circuitBoardRoot != null) ? circuitBoardRoot.transform.position + new Vector3(0.06f, -0.12f, -0.04f) : new Vector3(0, 0.1f, 1.4f);
        boardFlamesPS = CreateParticleChild("BoardFlames", boardFlamesPos, pMat, fireRoot.transform);
        var bfMain = boardFlamesPS.main;
        bfMain.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        bfMain.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 4.6f);
        bfMain.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.15f);
        bfMain.simulationSpace = ParticleSystemSimulationSpace.World;
        boardFlamesEmission = boardFlamesPS.emission;
        boardFlamesEmission.rateOverTime = 170f;

        var bfShape = boardFlamesPS.shape;
        bfShape.shapeType = ParticleSystemShapeType.Cone;
        bfShape.angle = 18f;
        bfShape.radius = 0.12f;
        bfShape.rotation = new Vector3(-90f, 0, 0);

        var bfCol = boardFlamesPS.colorOverLifetime;
        bfCol.enabled = true;
        bfCol.color = GetFlameGradient();

        var bfSize = boardFlamesPS.sizeOverLifetime;
        bfSize.enabled = true;
        bfSize.size = new ParticleSystem.MinMaxCurve(1f, fCurve);

        var bfNoise = boardFlamesPS.noise;
        bfNoise.enabled = true;
        bfNoise.strength = 0.5f;

        // --- LAYER 3: WHITE-HOT INCANDESCENT CORE ---
        electricalCorePS = CreateParticleChild("ElectricalCore", groundCenter + new Vector3(0, 0.05f, 0), pMat, fireRoot.transform);
        var ecMain = electricalCorePS.main;
        ecMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        ecMain.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
        ecMain.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
        ecMain.simulationSpace = ParticleSystemSimulationSpace.World;
        electricalCoreEmission = electricalCorePS.emission;
        electricalCoreEmission.rateOverTime = 120f;

        var ecShape = electricalCorePS.shape;
        ecShape.shapeType = ParticleSystemShapeType.Cone;
        ecShape.angle = 6f;
        ecShape.radius = 0.18f;
        ecShape.rotation = new Vector3(-90f, 0, 0);

        var ecCol = electricalCorePS.colorOverLifetime;
        ecCol.enabled = true;
        ecCol.color = GetCoreGradient();

        // --- LAYER 4: A LOT OF MASSIVE BILLOWING BLACK SMOKE (FLOOR + BOARD) ---
        heavyFloorSmokePS = CreateParticleChild("HeavyFloorSmoke", groundCenter + new Vector3(0, 0.7f, 0), pMat, fireRoot.transform);
        var smMain = heavyFloorSmokePS.main;
        smMain.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 4.6f);
        smMain.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        smMain.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        smMain.simulationSpace = ParticleSystemSimulationSpace.World;
        heavyFloorSmokeEmission = heavyFloorSmokePS.emission;
        heavyFloorSmokeEmission.rateOverTime = 95f; // A LOT of smoke!

        var smShape = heavyFloorSmokePS.shape;
        smShape.shapeType = ParticleSystemShapeType.Cone;
        smShape.angle = 20f;
        smShape.radius = 0.2f;
        smShape.rotation = new Vector3(-90f, 0, 0);

        var smCol = heavyFloorSmokePS.colorOverLifetime;
        smCol.enabled = true;
        Gradient smGrad = new Gradient();
        smGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.02f, 0.02f, 0.02f), 0f), // Deep carbon black
                new GradientColorKey(new Color(0.08f, 0.08f, 0.09f), 0.6f),
                new GradientColorKey(new Color(0.16f, 0.16f, 0.16f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.2f),
                new GradientAlphaKey(0.7f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        smCol.color = smGrad;

        var smSize = heavyFloorSmokePS.sizeOverLifetime;
        smSize.enabled = true;
        AnimationCurve smCurve = new AnimationCurve();
        smCurve.AddKey(0f, 0.35f);
        smCurve.AddKey(0.35f, 1.3f);
        smCurve.AddKey(1f, 3.2f); // Billows broadly across the ceiling
        smSize.size = new ParticleSystem.MinMaxCurve(1f, smCurve);

        var smNoise = heavyFloorSmokePS.noise;
        smNoise.enabled = true;
        smNoise.strength = 0.55f;

        // Additional Board Smoke
        boardSmokePS = CreateParticleChild("BoardSmoke", boardFlamesPos + new Vector3(0, 0.3f, 0), pMat, fireRoot.transform);
        var bsmMain = boardSmokePS.main;
        bsmMain.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
        bsmMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        bsmMain.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        bsmMain.simulationSpace = ParticleSystemSimulationSpace.World;
        boardSmokeEmission = boardSmokePS.emission;
        boardSmokeEmission.rateOverTime = 55f;

        // NOTE: shape/colorOverLifetime/sizeOverLifetime are structs returned by
        // value from these properties, so members can't be set directly on the
        // property result (that's the CS1612 error). Pull each into a local
        // variable first, mutate the local, and Unity applies it back to the
        // particle system automatically.
        var bsmShape = boardSmokePS.shape;
        bsmShape.shapeType = ParticleSystemShapeType.Cone;

        var bsmCol = boardSmokePS.colorOverLifetime;
        bsmCol.enabled = true;
        bsmCol.color = smGrad;

        var bsmSize = boardSmokePS.sizeOverLifetime;
        bsmSize.enabled = true;
        bsmSize.size = new ParticleSystem.MinMaxCurve(1f, smCurve);

        // Dynamic Orange Room Light
        GameObject fLight = new GameObject("ElectricalFireLight");
        fLight.transform.SetParent(fireRoot.transform, false);
        fLight.transform.localPosition = new Vector3(0, 0.55f, 0);
        fireLight = fLight.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.48f, 0.12f);
        fireLight.range = 7.5f;
        fireLight.intensity = 6.0f;
    }

    // =========================================================================
    // 4. 3D CO2 EXTINGUISHER
    // =========================================================================
    private void Build3DExtinguisher()
    {
        extinguisherRoot = new GameObject("3D_CO2_Extinguisher");
        extinguisherRoot.transform.SetParent(transform, false);

        Shader solidShader = GetCompatibleSolidShader();
        Material redMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.12f, 0.1f));
        Material blackMat = CreateSolidMaterial(solidShader, new Color(0.15f, 0.15f, 0.16f));
        Material brassMat = CreateSolidMaterial(solidShader, new Color(0.8f, 0.65f, 0.25f));

        CreateSubCylinder(extinguisherRoot.transform, Vector3.zero, new Vector3(0.18f, 0.36f, 0.18f), redMat, Vector3.zero);
        CreateSubCylinder(extinguisherRoot.transform, new Vector3(0, 0.38f, 0), new Vector3(0.065f, 0.05f, 0.065f), brassMat, Vector3.zero);
        CreateSubCube(extinguisherRoot.transform, new Vector3(0, 0.45f, -0.05f), new Vector3(0.026f, 0.1f, 0.15f), blackMat);
        CreateSubCylinder(extinguisherRoot.transform, new Vector3(0.1f, 0.23f, 0.15f), new Vector3(0.078f, 0.19f, 0.078f), blackMat, new Vector3(45f, 0, 0));

        var col = extinguisherRoot.AddComponent<BoxCollider>();
        col.size = new Vector3(0.32f, 0.65f, 0.32f);
        extinguisherDraggable = extinguisherRoot.AddComponent<Draggable3DExtinguisher>();

        Material pMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSoftCircleTexture(64) };
        co2SprayPS = CreateParticleChild("CO2_GasSpray", new Vector3(0.08f, 0.12f, 0.18f), pMat, extinguisherRoot.transform);
        var spMain = co2SprayPS.main;
        spMain.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        spMain.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 4.5f);
        spMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        spMain.simulationSpace = ParticleSystemSimulationSpace.World;
        co2SprayEmission = co2SprayPS.emission;
        co2SprayEmission.rateOverTime = 0f;

        var spShape = co2SprayPS.shape;
        spShape.shapeType = ParticleSystemShapeType.Cone;
        spShape.angle = 12f;
        spShape.radius = 0.02f;
        spShape.rotation = new Vector3(45f, 0, 0);

        var spCol = co2SprayPS.colorOverLifetime;
        spCol.enabled = true;
        Gradient spGrad = new Gradient();
        spGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.95f, 1f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        spCol.color = spGrad;

        extinguisherRoot.SetActive(false);
    }

    // =========================================================================
    // 5. AUTO-GENERATED UI
    // =========================================================================
    private void BuildProceduralUI()
    {
        GameObject canvasObj = new GameObject("Simulation_Canvas");
        uiCanvas = canvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // Tuned for portrait phones. Without this, the default 800x600
        // reference plus "match width" scaling makes text wrap differently
        // on tall narrow screens than it does on desktop, which is what was
        // pushing the description text down into the answer buttons.
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f; // Match height only - keeps text a consistent, readable size across different phone aspect ratios
        canvasObj.AddComponent<GraphicRaycaster>();

        Font defaultFont = GetUniversalFont();

        // Top Banner
        GameObject bannerObj = new GameObject("TopBanner");
        bannerObj.transform.SetParent(canvasObj.transform, false);
        bannerBg = bannerObj.AddComponent<Image>();
        bannerBg.color = new Color(0.85f, 0.2f, 0.2f);
        RectTransform bannerRT = bannerObj.GetComponent<RectTransform>();
        bannerRT.anchorMin = new Vector2(0f, 1f);
        bannerRT.anchorMax = new Vector2(1f, 1f);
        bannerRT.pivot = new Vector2(0.5f, 1f);
        bannerRT.sizeDelta = new Vector2(0, 70f);

        GameObject bannerTextObj = new GameObject("BannerText");
        bannerTextObj.transform.SetParent(bannerObj.transform, false);
        bannerText = bannerTextObj.AddComponent<Text>();
        bannerText.font = defaultFont;
        bannerText.fontSize = 26;
        bannerText.fontStyle = FontStyle.Bold;
        bannerText.alignment = TextAnchor.MiddleCenter;
        bannerText.color = Color.white;
        RectTransform btRT = bannerTextObj.GetComponent<RectTransform>();
        btRT.anchorMin = Vector2.zero;
        btRT.anchorMax = Vector2.one;
        btRT.sizeDelta = Vector2.zero;

        // Center Question Card
        questionCard = new GameObject("QuestionCard");
        questionCard.transform.SetParent(canvasObj.transform, false);
        Image qcBg = questionCard.AddComponent<Image>();
        qcBg.color = new Color(0.1f, 0.12f, 0.15f, 0.94f);
        RectTransform qcRT = questionCard.GetComponent<RectTransform>();
        qcRT.anchorMin = new Vector2(0.5f, 0.5f);
        qcRT.anchorMax = new Vector2(0.5f, 0.5f);
        qcRT.sizeDelta = new Vector2(620f, 520f); // Extra height to fit the larger, more legible fonts

        // Title
        GameObject titleObj = new GameObject("QTitle");
        titleObj.transform.SetParent(questionCard.transform, false);
        questionTitleText = titleObj.AddComponent<Text>();
        questionTitleText.font = defaultFont;
        questionTitleText.fontSize = 26;
        questionTitleText.fontStyle = FontStyle.Bold;
        questionTitleText.color = new Color(1f, 0.8f, 0.2f);
        questionTitleText.alignment = TextAnchor.MiddleCenter;
        questionTitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        questionTitleText.verticalOverflow = VerticalWrapMode.Truncate;
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0, -20f);
        titleRT.sizeDelta = new Vector2(-40f, 46f);

        // Description
        GameObject descObj = new GameObject("QDesc");
        descObj.transform.SetParent(questionCard.transform, false);
        questionDescText = descObj.AddComponent<Text>();
        questionDescText.font = defaultFont;
        questionDescText.fontSize = 19;
        questionDescText.color = new Color(0.9f, 0.9f, 0.9f);
        questionDescText.alignment = TextAnchor.UpperCenter;
        questionDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
        questionDescText.verticalOverflow = VerticalWrapMode.Truncate; // Never overflow into the buttons below
        RectTransform descRT = descObj.GetComponent<RectTransform>();
        descRT.anchorMin = new Vector2(0, 1);
        descRT.anchorMax = new Vector2(1, 1);
        descRT.pivot = new Vector2(0.5f, 1f);
        descRT.anchoredPosition = new Vector2(0, -74f);
        descRT.sizeDelta = new Vector2(-50f, 130f); // Enough height for 4-5 wrapped lines at the larger font size

        // 3 Option Buttons
        float buttonYStart = -220f; // Clear gap below the (taller) description block
        float buttonSpacing = 80f;  // Extra room between buttons so they never touch
        for (int i = 0; i < 3; i++)
        {
            GameObject btnObj = new GameObject("OptionBtn_" + i);
            btnObj.transform.SetParent(questionCard.transform, false);
            Image btnImg = btnObj.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.24f, 0.3f);
            Button btn = btnObj.AddComponent<Button>();
            optionButtons[i] = btn;

            RectTransform btnRT = btnObj.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0.5f, 1f);
            btnRT.anchorMax = new Vector2(0.5f, 1f);
            btnRT.pivot = new Vector2(0.5f, 1f);
            btnRT.sizeDelta = new Vector2(560f, 62f);
            btnRT.anchoredPosition = new Vector2(0, buttonYStart - i * buttonSpacing);

            GameObject btnTextObj = new GameObject("BtnText");
            btnTextObj.transform.SetParent(btnObj.transform, false);
            Text btnText = btnTextObj.AddComponent<Text>();
            btnText.font = defaultFont;
            btnText.fontSize = 20;
            btnText.fontStyle = FontStyle.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAnchor.MiddleCenter;
            btnText.horizontalOverflow = HorizontalWrapMode.Wrap;
            btnText.verticalOverflow = VerticalWrapMode.Truncate;
            optionButtonTexts[i] = btnText;

            RectTransform btTextRT = btnTextObj.GetComponent<RectTransform>();
            btTextRT.anchorMin = Vector2.zero;
            btTextRT.anchorMax = Vector2.one;
            btTextRT.sizeDelta = new Vector2(-16f, 0f); // Small horizontal padding so text doesn't touch button edges
        }

        // Minigame Extinguish HUD
        minigameHud = new GameObject("MinigameHUD");
        minigameHud.transform.SetParent(canvasObj.transform, false);
        RectTransform mgRT = minigameHud.AddComponent<RectTransform>();
        mgRT.anchorMin = new Vector2(0.5f, 0f);
        mgRT.anchorMax = new Vector2(0.5f, 0f);
        mgRT.pivot = new Vector2(0.5f, 0f);
        mgRT.anchoredPosition = new Vector2(0, 30f);
        mgRT.sizeDelta = new Vector2(500f, 90f);

        Image mgBg = minigameHud.AddComponent<Image>();
        mgBg.color = new Color(0.1f, 0.12f, 0.15f, 0.9f);

        GameObject sliderObj = new GameObject("FireSlider");
        sliderObj.transform.SetParent(minigameHud.transform, false);
        fireHealthBar = sliderObj.AddComponent<Slider>();
        fireHealthBar.minValue = 0f;
        fireHealthBar.maxValue = 1f;
        fireHealthBar.value = 1f;
        RectTransform slRT = sliderObj.GetComponent<RectTransform>();
        slRT.sizeDelta = new Vector2(440f, 25f);
        slRT.anchoredPosition = new Vector2(0, -10f);

        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(sliderObj.transform, false);
        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.color = new Color(1f, 0.35f, 0.1f);
        RectTransform fillRT = fillObj.GetComponent<RectTransform>();
        fillRT.sizeDelta = Vector2.zero;
        fireHealthBar.fillRect = fillRT;

        GameObject hudTextObj = new GameObject("HUDText");
        hudTextObj.transform.SetParent(minigameHud.transform, false);
        Text hudText = hudTextObj.AddComponent<Text>();
        hudText.font = defaultFont;
        hudText.fontSize = 19;
        hudText.fontStyle = FontStyle.Bold;
        hudText.color = Color.white;
        hudText.alignment = TextAnchor.MiddleCenter;
        hudText.text = "EXTINGUISHING FIRE (Drag Extinguisher over fire base)";
        RectTransform htRT = hudTextObj.GetComponent<RectTransform>();
        htRT.anchoredPosition = new Vector2(0, 20f);
        htRT.sizeDelta = new Vector2(480f, 30f);

        minigameHud.SetActive(false);

        // Success Card
        successCard = new GameObject("SuccessCard");
        successCard.transform.SetParent(canvasObj.transform, false);
        Image sBg = successCard.AddComponent<Image>();
        sBg.color = new Color(0.12f, 0.18f, 0.15f, 0.95f);
        RectTransform sRT = successCard.GetComponent<RectTransform>();
        sRT.anchorMin = new Vector2(0.5f, 0.5f);
        sRT.anchorMax = new Vector2(0.5f, 0.5f);
        sRT.sizeDelta = new Vector2(520f, 270f);

        GameObject sTitleObj = new GameObject("STitle");
        sTitleObj.transform.SetParent(successCard.transform, false);
        Text sTitle = sTitleObj.AddComponent<Text>();
        sTitle.font = defaultFont;
        sTitle.fontSize = 30;
        sTitle.fontStyle = FontStyle.Bold;
        sTitle.color = new Color(0.3f, 1f, 0.45f);
        sTitle.alignment = TextAnchor.MiddleCenter;
        sTitle.text = "EVACUATION COMPLETE!";
        RectTransform stRT = sTitleObj.GetComponent<RectTransform>();
        stRT.anchoredPosition = new Vector2(0, 65f);
        stRT.sizeDelta = new Vector2(480f, 40f);

        GameObject sDescObj = new GameObject("SDesc");
        sDescObj.transform.SetParent(successCard.transform, false);
        Text sDesc = sDescObj.AddComponent<Text>();
        sDesc.font = defaultFont;
        sDesc.fontSize = 20;
        sDesc.color = Color.white;
        sDesc.alignment = TextAnchor.MiddleCenter;
        sDesc.text = "Training successful!\nYou equipped proper PPE, deployed CO2, aimed at the fuel base, and identified the safe, undamaged emergency exit to escape!";
        RectTransform sdRT = sDescObj.GetComponent<RectTransform>();
        sdRT.anchoredPosition = new Vector2(0, 10f);
        sdRT.sizeDelta = new Vector2(460f, 65f);

        // Restart Button
        GameObject rBtnObj = new GameObject("RestartBtn");
        rBtnObj.transform.SetParent(successCard.transform, false);
        Image rBtnImg = rBtnObj.AddComponent<Image>();
        rBtnImg.color = new Color(0.15f, 0.6f, 0.28f);
        Button rBtn = rBtnObj.AddComponent<Button>();
        rBtn.onClick.AddListener(RestartSimulation);
        RectTransform rbRT = rBtnObj.GetComponent<RectTransform>();
        rbRT.anchoredPosition = new Vector2(0, -65f);
        rbRT.sizeDelta = new Vector2(240f, 50f);

        GameObject rTextObj = new GameObject("RText");
        rTextObj.transform.SetParent(rBtnObj.transform, false);
        Text rText = rTextObj.AddComponent<Text>();
        rText.font = defaultFont;
        rText.fontSize = 21;
        rText.fontStyle = FontStyle.Bold;
        rText.color = Color.white;
        rText.alignment = TextAnchor.MiddleCenter;
        rText.text = "Restart Simulation";
        RectTransform rtRT = rTextObj.GetComponent<RectTransform>();
        rtRT.anchorMin = Vector2.zero;
        rtRT.anchorMax = Vector2.one;
        rtRT.sizeDelta = Vector2.zero;

        successCard.SetActive(false);
    }

    // =========================================================================
    // 6. PROCEDURAL EMERGENCY FIRE ALARM SIREN
    // =========================================================================
    private void BuildAlarmAudio()
    {
        alarmSource = gameObject.AddComponent<AudioSource>();
        alarmSource.clip = GenerateIndustrialAlarmClip();
        alarmSource.loop = true;
        alarmSource.playOnAwake = false;
        alarmSource.spatialBlend = 0.15f;
        alarmSource.volume = 0.55f;
        alarmSource.Play();
    }

    private AudioClip GenerateIndustrialAlarmClip()
    {
        int sampleRate = 44100;
        float duration = 1.0f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float freq = (t < 0.5f) ? 880f : 660f;
            float tone = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * (freq * 2f) * t) * 0.3f;
            samples[i] = Mathf.Clamp(tone, -0.88f, 0.88f) * 0.5f;
        }

        AudioClip clip = AudioClip.Create("IndustrialFireAlarm", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // =========================================================================
    // 7. SAFE HELPERS & TEXTURES
    // =========================================================================
    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();

            System.Type inputSystemType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemType != null)
            {
                es.AddComponent(inputSystemType);
            }
            else
            {
                es.AddComponent<StandaloneInputModule>();
            }
        }
    }

    private void EnsurePhysicsRaycaster()
    {
        if (Camera.main != null)
        {
            if (Camera.main.GetComponent<PhysicsRaycaster>() == null)
            {
                Camera.main.gameObject.AddComponent<PhysicsRaycaster>();
            }

            #if UNITY_EDITOR
            var arBg = Camera.main.GetComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
            if (arBg != null)
            {
                arBg.enabled = false;
            }
            #endif
        }
    }

    // Replaces the default skybox (which renders as a yellow/orange
    // horizon-and-sun gradient) with a flat dark background that suits
    // the fire/smoke scene. Skip this if the project uses AR Foundation
    // camera passthrough, since Solid Color would hide the live feed too.
    private void SetCameraSolidBackground()
    {
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
        }
    }

    private Font GetUniversalFont()
    {
        Font f = (Font)Resources.GetBuiltinResource(typeof(Font), "LegacyRuntime.ttf");
        if (f == null) f = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return f;
    }

    private void CreateSubCube(Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.transform.SetParent(parent, false);
        c.transform.localPosition = localPos;
        c.transform.localScale = scale;
        Destroy(c.GetComponent<Collider>());
        c.GetComponent<Renderer>().material = mat;
    }

    private void CreateSubCylinder(Transform parent, Vector3 localPos, Vector3 scale, Material mat, Vector3 angles)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        c.transform.SetParent(parent, false);
        c.transform.localPosition = localPos;
        if (angles != Vector3.zero) c.transform.localRotation = Quaternion.Euler(angles);
        c.transform.localScale = scale;
        Destroy(c.GetComponent<Collider>());
        c.GetComponent<Renderer>().material = mat;
    }

    private void BuildWireSegment(Transform parent, Vector3 start, Vector3 end, Material mat, float thickness)
    {
        GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        seg.transform.SetParent(parent, false);
        Vector3 mid = (start + end) * 0.5f;
        seg.transform.position = mid;
        seg.transform.up = (end - start).normalized;
        seg.transform.localScale = new Vector3(thickness, Vector3.Distance(start, end) * 0.5f, thickness);
        Destroy(seg.GetComponent<Collider>());
        seg.GetComponent<Renderer>().material = mat;
    }

    private ParticleSystem CreateParticleChild(string name, Vector3 pos, Material mat, Transform parent)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = pos;

        var ps = child.AddComponent<ParticleSystem>();
        var rend = child.GetComponent<ParticleSystemRenderer>();
        rend.material = mat;
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder = 5;

        return ps;
    }

    private Shader GetCompatibleSolidShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (s == null) s = Shader.Find("Standard");
        if (s == null) s = Shader.Find("Mobile/Diffuse");
        if (s == null) s = Shader.Find("Sprites/Default");
        return s;
    }

    private Material CreateSolidMaterial(Shader shader, Color col)
    {
        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        mat.color = col;
        return mat;
    }

    private Texture2D GenerateTileTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color baseCol = new Color(0.7f, 0.7f, 0.72f);
        Color seamCol = new Color(0.44f, 0.44f, 0.46f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isSeam = (x < 2 || x >= size - 2 || y < 2 || y >= size - 2);
                tex.SetPixel(x, y, isSeam ? seamCol : baseCol);
            }
        }
        tex.Apply();
        return tex;
    }

    private Texture2D GenerateSoftCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float norm = Mathf.Clamp01(dist / maxDist);
                float alpha = Mathf.Pow(Mathf.Cos(norm * Mathf.PI * 0.5f), 2.2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private Texture2D GenerateSootTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float norm = Mathf.Clamp01(dist / maxDist);
                float alpha = Mathf.Clamp01(Mathf.Pow(1f - norm, 1.8f) * 0.88f);
                tex.SetPixel(x, y, new Color(0.04f, 0.03f, 0.03f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private Gradient GetFlameGradient()
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),   // Incandescent core
                new GradientColorKey(new Color(1f, 0.50f, 0.02f), 0.3f), // Orange blaze
                new GradientColorKey(new Color(0.92f, 0.12f, 0f), 0.72f),// Intense red
                new GradientColorKey(new Color(0.18f, 0.02f, 0.02f), 1f) // Dark tip
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.92f, 0.12f),
                new GradientAlphaKey(0.68f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        return grad;
    }

    private Gradient GetCoreGradient()
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.95f, 0.4f), 0.5f),
                new GradientColorKey(new Color(1f, 0.45f, 0f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.95f, 0.1f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        return grad;
    }
}

// =========================================================================
// INTERACTIVE 3D DOOR CLICK HANDLER (SAFE vs DAMAGED EXIT)
// =========================================================================
public class DoorClickHandler : MonoBehaviour, IPointerClickHandler
{
    private FireSafetyManager manager;
    private bool isSafeDoor;

    public void Init(FireSafetyManager mgr, bool safe)
    {
        manager = mgr;
        isSafeDoor = safe;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (manager != null)
        {
            manager.OnDoorClicked(isSafeDoor);
        }
    }
}

// =========================================================================
// DRAGGABLE EXTINGUISHER
// =========================================================================
public class Draggable3DExtinguisher : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    private Camera mainCam;

    // Once the player grabs the extinguisher, the manager's per-frame
    // camera-follow must stop overriding their drag input - otherwise the
    // extinguisher snaps back to the camera every frame and can never
    // actually be dragged.
    public bool HasBeenGrabbed { get; private set; }

    void Start()
    {
        mainCam = Camera.main;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        HasBeenGrabbed = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        float zDepth = mainCam.WorldToScreenPoint(transform.position).z;
        Vector3 screenPos = new Vector3(eventData.position.x, eventData.position.y, zDepth);
        transform.position = mainCam.ScreenToWorldPoint(screenPos);
    }

    // Called when a new attempt starts so the extinguisher docks back to
    // the camera-follow position at the start of each minigame.
    public void ResetGrabState()
    {
        HasBeenGrabbed = false;
    }
}