using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class FireSafetyManager : MonoBehaviour
{
    private enum SimState { Question_PPE, Question_Extinguisher, Question_Aim, Minigame_Extinguishing, Success }
    private SimState currentState;

    // 3D Environment Roots
    private GameObject roomRoot;
    private GameObject switchboardRoot;
    private GameObject fireRoot;
    private GameObject extinguisherRoot;

    // Particle Systems
    private ParticleSystem mainFlamesPS;
    private ParticleSystem coreFlamesPS;
    private ParticleSystem coalsPS;
    private ParticleSystem embersPS;
    private ParticleSystem smokePS;
    private ParticleSystem sparksPS;
    private ParticleSystem co2SprayPS;

    // Cached Emission Modules
    private ParticleSystem.EmissionModule mainFlamesEmission;
    private ParticleSystem.EmissionModule coreFlamesEmission;
    private ParticleSystem.EmissionModule coalsEmission;
    private ParticleSystem.EmissionModule embersEmission;
    private ParticleSystem.EmissionModule smokeEmission;
    private ParticleSystem.EmissionModule sparksEmission;
    private ParticleSystem.EmissionModule co2SprayEmission;

    private Light fireLight;
    private Light sparkLight;
    private AudioSource alarmSource;

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
        EnsurePhysicsRaycaster();
        Build3DRoomAndSwitchboard();
        BuildShapedGroundFire();
        Build3DExtinguisher();
        BuildProceduralUI();
        BuildAlarmAudio();
    }

    void Start()
    {
        StartPPEQuestion();
    }

    void Update()
    {
        // Dynamic flame flicker
        if (fireLight != null && fireLight.enabled)
        {
            float noise = Mathf.PerlinNoise(Time.time * 8.5f, 0f);
            fireLight.intensity = 2.8f * (0.8f + noise * 0.45f);
        }

        // Electric arc flashes at cut wires
        if (sparkLight != null && sparkLight.enabled)
        {
            sparkLight.intensity = (Random.value > 0.8f) ? Random.Range(2.0f, 4.0f) : 0f;
        }

        // Extinguish progress
        if (currentState == SimState.Minigame_Extinguishing && isExtinguishing)
        {
            fireHealth -= extinguishRate * Time.deltaTime;
            fireHealth = Mathf.Clamp01(fireHealth);

            if (fireHealthBar != null) fireHealthBar.value = fireHealth;
            ApplyFireHealth(fireHealth);

            if (fireHealth <= 0f)
            {
                CompleteSimulation();
            }
        }
    }

    // =========================================================================
    // 1. QUESTION FLOW
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

        SetBanner("ELECTRICAL FIRE DETECTED!", new Color(0.85f, 0.2f, 0.2f));
        questionTitleText.text = "Step 1: Select Appropriate PPE";
        questionDescText.text = "A live electrical short-circuit has caused a fire. What gear must you equip before approaching?";

        SetOption(0, "Class 0 Insulated Rubber Gloves & Arc Flash Face Shield", () => {
            SetBanner("CORRECT: Insulated gear protects against lethal shock & arc flashes.", new Color(0.15f, 0.65f, 0.25f));
            Invoke(nameof(StartExtinguisherQuestion), 1.4f);
        });

        SetOption(1, "Standard Cotton Clothes & Leather Work Gloves", () => {
            SetBanner("INCORRECT: Cotton & standard gloves conduct electricity and can catch fire!", new Color(0.8f, 0.2f, 0.2f));
        });

        SetOption(2, "No Gear - Rush in immediately with a water bucket", () => {
            SetBanner("FATAL MISTAKE: Never approach live electrical fires without insulation!", new Color(0.85f, 0.1f, 0.1f));
        });
    }

    private void StartExtinguisherQuestion()
    {
        currentState = SimState.Question_Extinguisher;
        SetBanner("SELECT EXTINGUISHING AGENT", new Color(0.9f, 0.55f, 0.1f));
        questionTitleText.text = "Step 2: Choose the Extinguisher";
        questionDescText.text = "Which fire extinguisher is certified and safe for energized electrical equipment?";

        SetOption(0, "CO2 (Carbon Dioxide) / Dry Powder Extinguisher", () => {
            SetBanner("CORRECT: CO2 is non-conductive and starves the fire without shock risks.", new Color(0.15f, 0.65f, 0.25f));
            Invoke(nameof(StartAimQuestion), 1.4f);
        });

        SetOption(1, "Pressurized Water Jet Extinguisher (Class A)", () => {
            SetBanner("DANGEROUS: Water conducts electricity straight back to the user causing severe shock!", new Color(0.85f, 0.1f, 0.1f));
        });

        SetOption(2, "AFFF Foam Spray Extinguisher", () => {
            SetBanner("INCORRECT: Water-based foam creates an electrocution hazard on live circuits!", new Color(0.8f, 0.2f, 0.2f));
        });
    }

    private void StartAimQuestion()
    {
        currentState = SimState.Question_Aim;
        SetBanner("EXTINGUISHER TECHNIQUE", new Color(0.9f, 0.55f, 0.1f));
        questionTitleText.text = "Step 3: Pointing the Nozzle (P.A.S.S.)";
        questionDescText.text = "According to the P.A.S.S. protocol, where should you direct the extinguisher horn?";

        SetOption(0, "Aim at the base of the fire & sweep side-to-side", () => {
            SetBanner("EXCELLENT: Aiming at the base attacks the fuel source directly!", new Color(0.15f, 0.65f, 0.25f));
            Invoke(nameof(StartMinigame), 1.2f);
        });

        SetOption(1, "Aim high directly at the top of the flames", () => {
            SetBanner("INCORRECT: Aiming high only disperses gas into the air without stopping fuel combustion.", new Color(0.8f, 0.2f, 0.2f));
        });

        SetOption(2, "Aim into the smoke column above the fire", () => {
            SetBanner("WRONG: Extinguishing smoke does not extinguish the burning fuel beneath.", new Color(0.8f, 0.2f, 0.2f));
        });
    }

    private void StartMinigame()
    {
        currentState = SimState.Minigame_Extinguishing;
        isExtinguishing = true;
        if (questionCard != null) questionCard.SetActive(false);
        if (minigameHud != null) minigameHud.SetActive(true);

        SetBanner("EXTINGUISHING IN PROGRESS: Drag extinguisher over the fire base!", new Color(0.1f, 0.5f, 0.85f));

        if (extinguisherRoot != null)
        {
            extinguisherRoot.SetActive(true);
            if (Camera.main != null)
            {
                extinguisherRoot.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.85f - Camera.main.transform.up * 0.25f;
            }
        }

        if (co2SprayPS != null)
        {
            co2SprayEmission.rateOverTime = 60f;
            co2SprayPS.Play();
        }
    }

    private void CompleteSimulation()
    {
        currentState = SimState.Success;
        isExtinguishing = false;

        if (co2SprayPS != null) co2SprayEmission.rateOverTime = 0f;
        if (minigameHud != null) minigameHud.SetActive(false);
        if (successCard != null) successCard.SetActive(true);

        SetBanner("FIRE SAFELY EXTINGUISHED! ROOM SECURED.", new Color(0.12f, 0.7f, 0.3f));
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
        mainFlamesEmission.rateOverTime = 85f * norm;
        coreFlamesEmission.rateOverTime = 55f * norm;
        coalsEmission.rateOverTime = 35f * norm;
        embersEmission.rateOverTime = 40f * (norm * norm);
        smokeEmission.rateOverTime = 30f * (norm > 0.01f ? (0.25f + norm * 0.75f) : 0f);
        sparksEmission.rateOverTime = 45f * (norm * norm);

        if (fireLight != null)
        {
            fireLight.intensity = 2.8f * norm;
            fireLight.enabled = norm > 0.02f;
        }

        if (sparkLight != null) sparkLight.enabled = norm > 0.05f;

        if (alarmSource != null)
        {
            alarmSource.volume = 0.5f * norm;
            if (norm <= 0.01f && alarmSource.isPlaying) alarmSource.Stop();
            else if (norm > 0.01f && !alarmSource.isPlaying) alarmSource.Play();
        }
    }

    // =========================================================================
    // 2. 3D ROOM & WALL SWITCHBOARD
    // =========================================================================
    private void Build3DRoomAndSwitchboard()
    {
        roomRoot = new GameObject("3D_Room_Environment");
        roomRoot.transform.SetParent(transform, false);

        Shader solidShader = GetCompatibleSolidShader();
        Material floorMat = CreateSolidMaterial(solidShader, new Color(0.72f, 0.72f, 0.73f));
        floorMat.mainTexture = GenerateTileTexture(128);

        Material wallMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.84f, 0.82f));
        Material trimMat = CreateSolidMaterial(solidShader, new Color(0.25f, 0.25f, 0.27f));

        Vector3 center = Vector3.forward * 1.5f;
        if (Camera.main != null)
        {
            Vector3 camPos = Camera.main.transform.position;
            Vector3 fwdFlat = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            center = camPos + fwdFlat * 1.6f;
        }

        // Floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FloorSlab";
        floor.transform.SetParent(roomRoot.transform, false);
        floor.transform.position = center + new Vector3(0, -0.72f, 0);
        floor.transform.localScale = new Vector3(3.2f, 0.04f, 3.2f);
        floor.GetComponent<Renderer>().material = floorMat;

        // Back Wall
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "BackWall";
        wall.transform.SetParent(roomRoot.transform, false);
        wall.transform.position = center + new Vector3(0, 0.46f, 1.5f);
        wall.transform.localScale = new Vector3(3.2f, 2.4f, 0.06f);
        wall.GetComponent<Renderer>().material = wallMat;

        // Baseboard
        GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trim.name = "Baseboard";
        trim.transform.SetParent(roomRoot.transform, false);
        trim.transform.position = center + new Vector3(0, -0.66f, 1.46f);
        trim.transform.localScale = new Vector3(3.2f, 0.08f, 0.03f);
        trim.GetComponent<Renderer>().material = trimMat;

        // Wall Switchboard
        switchboardRoot = new GameObject("Wall_Switchboard");
        switchboardRoot.transform.SetParent(roomRoot.transform, false);
        Vector3 boardPos = center + new Vector3(0, 0.25f, 1.44f);
        switchboardRoot.transform.position = boardPos;

        Material boxMetal = CreateSolidMaterial(solidShader, new Color(0.22f, 0.23f, 0.25f));
        Material redMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.15f, 0.1f));
        Material blueMat = CreateSolidMaterial(solidShader, new Color(0.15f, 0.4f, 0.9f));
        Material copperMat = CreateSolidMaterial(solidShader, new Color(0.95f, 0.6f, 0.25f));

        CreateSubCube(switchboardRoot.transform, Vector3.zero, new Vector3(0.24f, 0.32f, 0.06f), boxMetal);
        CreateSubCube(switchboardRoot.transform, new Vector3(0, 0.04f, -0.032f), new Vector3(0.16f, 0.10f, 0.02f), CreateSolidMaterial(solidShader, new Color(0.08f, 0.06f, 0.05f)));

        Vector3 wireOrigin = boardPos + new Vector3(0.08f, -0.16f, -0.02f);
        BuildWireSegment(switchboardRoot.transform, wireOrigin, boardPos + new Vector3(0.07f, -0.26f, -0.04f), redMat, 0.009f);
        BuildWireSegment(switchboardRoot.transform, wireOrigin + new Vector3(-0.02f, 0, 0), boardPos + new Vector3(0.05f, -0.28f, -0.03f), blueMat, 0.009f);

        CreateSubCylinder(switchboardRoot.transform, new Vector3(0.07f, -0.27f, -0.04f), new Vector3(0.007f, 0.016f, 0.007f), copperMat, Vector3.zero);
        CreateSubCylinder(switchboardRoot.transform, new Vector3(0.05f, -0.29f, -0.03f), new Vector3(0.007f, 0.016f, 0.007f), copperMat, Vector3.zero);

        // Wall scorch mark
        GameObject wallSoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wallSoot.transform.SetParent(switchboardRoot.transform, false);
        wallSoot.transform.position = boardPos + new Vector3(0.04f, -0.08f, -0.035f);
        wallSoot.transform.localScale = new Vector3(0.42f, 0.48f, 1f);
        Destroy(wallSoot.GetComponent<Collider>());
        wallSoot.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSootTexture(64) };

        // Sparks
        Material pMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSoftCircleTexture(64) };
        sparksPS = CreateParticleChild("WallSparks", wireOrigin + new Vector3(0, -0.1f, 0), pMat, switchboardRoot.transform);
        var spMain = sparksPS.main;
        spMain.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        spMain.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 4.2f);
        spMain.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
        spMain.gravityModifier = 0.95f;
        spMain.simulationSpace = ParticleSystemSimulationSpace.World;
        sparksEmission = sparksPS.emission;
        sparksEmission.rateOverTime = 45f;

        var spShape = sparksPS.shape;
        spShape.shapeType = ParticleSystemShapeType.Sphere;
        spShape.radius = 0.02f;

        var spCol = sparksPS.colorOverLifetime;
        spCol.enabled = true;
        Gradient spGrad = new Gradient();
        spGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(0.4f, 0.85f, 1f), 0f), new GradientColorKey(new Color(1f, 0.95f, 0.3f), 0.35f), new GradientColorKey(new Color(1f, 0.35f, 0f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        spCol.color = spGrad;

        // Spark Flash Light
        GameObject sLight = new GameObject("SparkFlashLight");
        sLight.transform.SetParent(switchboardRoot.transform, false);
        sLight.transform.position = wireOrigin + new Vector3(0, -0.05f, -0.1f);
        sparkLight = sLight.AddComponent<Light>();
        sparkLight.type = LightType.Point;
        sparkLight.color = new Color(0.4f, 0.75f, 1f);
        sparkLight.range = 2.4f;
        sparkLight.intensity = 0f;
    }

    // =========================================================================
    // 3. TAPERED GROUND FIRE
    // =========================================================================
    private void BuildShapedGroundFire()
    {
        fireRoot = new GameObject("Ground_Fire_System");
        fireRoot.transform.SetParent(transform, false);

        Vector3 groundCenter = Vector3.forward * 1.5f + new Vector3(0, -0.68f, 0.45f);
        if (switchboardRoot != null)
        {
            groundCenter = new Vector3(switchboardRoot.transform.position.x + 0.05f, -0.68f, switchboardRoot.transform.position.z - 0.75f);
        }
        fireRoot.transform.position = groundCenter;

        Shader solidShader = GetCompatibleSolidShader();
        Material logMat = CreateSolidMaterial(solidShader, new Color(0.08f, 0.06f, 0.05f));
        Material coalGlowMat = CreateSolidMaterial(solidShader, new Color(0.9f, 0.25f, 0.05f));

        GameObject groundSoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        groundSoot.name = "GroundScorchMark";
        groundSoot.transform.SetParent(fireRoot.transform, false);
        groundSoot.transform.localPosition = new Vector3(0, 0.002f, 0);
        groundSoot.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        groundSoot.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
        Destroy(groundSoot.GetComponent<Collider>());
        groundSoot.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSootTexture(64) };

        CreateSubCylinder(fireRoot.transform, new Vector3(0, 0.03f, 0), new Vector3(0.07f, 0.38f, 0.07f), logMat, new Vector3(8, 25, 5));
        CreateSubCylinder(fireRoot.transform, new Vector3(0.02f, 0.035f, -0.02f), new Vector3(0.06f, 0.36f, 0.06f), logMat, new Vector3(-8, -40, 10));
        CreateSubCylinder(fireRoot.transform, new Vector3(-0.02f, 0.05f, 0.01f), new Vector3(0.055f, 0.32f, 0.055f), logMat, new Vector3(12, 75, -8));
        CreateSubCube(fireRoot.transform, new Vector3(0, 0.03f, 0), new Vector3(0.12f, 0.06f, 0.12f), coalGlowMat);

        Material pMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = GenerateSoftCircleTexture(64) };

        // Main Flames (Tapered)
        mainFlamesPS = CreateParticleChild("MainFlames_Tapered", new Vector3(0, 0.06f, 0), pMat, fireRoot.transform);
        var mfMain = mainFlamesPS.main;
        mfMain.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
        mfMain.startSpeed = new ParticleSystem.MinMaxCurve(0.85f, 1.7f);
        mfMain.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.58f);
        mfMain.simulationSpace = ParticleSystemSimulationSpace.World;
        mainFlamesEmission = mainFlamesPS.emission;
        mainFlamesEmission.rateOverTime = 85f;

        var mfShape = mainFlamesPS.shape;
        mfShape.shapeType = ParticleSystemShapeType.Cone;
        mfShape.angle = 8f;
        mfShape.radius = 0.16f;
        mfShape.rotation = new Vector3(-90f, 0, 0);

        var mfCol = mainFlamesPS.colorOverLifetime;
        mfCol.enabled = true;
        mfCol.color = GetFlameGradient();

        var mfSize = mainFlamesPS.sizeOverLifetime;
        mfSize.enabled = true;
        AnimationCurve tCurve = new AnimationCurve();
        tCurve.AddKey(0f, 0.45f);
        tCurve.AddKey(0.2f, 1.0f);
        tCurve.AddKey(0.65f, 0.55f);
        tCurve.AddKey(1f, 0.05f);
        mfSize.size = new ParticleSystem.MinMaxCurve(1f, tCurve);

        var mfNoise = mainFlamesPS.noise;
        mfNoise.enabled = true;
        mfNoise.strength = 0.38f;
        mfNoise.frequency = 0.55f;
        mfNoise.scrollSpeed = 1.3f;

        // Core Flames
        coreFlamesPS = CreateParticleChild("CoreFlames", new Vector3(0, 0.06f, 0), pMat, fireRoot.transform);
        var cMain = coreFlamesPS.main;
        cMain.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
        cMain.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
        cMain.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        cMain.simulationSpace = ParticleSystemSimulationSpace.World;
        coreFlamesEmission = coreFlamesPS.emission;
        coreFlamesEmission.rateOverTime = 55f;

        var cShape = coreFlamesPS.shape;
        cShape.shapeType = ParticleSystemShapeType.Cone;
        cShape.angle = 5f;
        cShape.radius = 0.10f;
        cShape.rotation = new Vector3(-90f, 0, 0);

        var cCol = coreFlamesPS.colorOverLifetime;
        cCol.enabled = true;
        cCol.color = GetCoreGradient();

        // Glowing Coals
        coalsPS = CreateParticleChild("GlowingCoals", new Vector3(0, 0.02f, 0), pMat, fireRoot.transform);
        var gcMain = coalsPS.main;
        gcMain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        gcMain.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        gcMain.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.26f);
        gcMain.simulationSpace = ParticleSystemSimulationSpace.World;
        coalsEmission = coalsPS.emission;
        coalsEmission.rateOverTime = 35f;

        var gcShape = coalsPS.shape;
        gcShape.shapeType = ParticleSystemShapeType.Circle;
        gcShape.radius = 0.18f;
        gcShape.rotation = new Vector3(-90f, 0, 0);

        var gcCol = coalsPS.colorOverLifetime;
        gcCol.enabled = true;
        Gradient gcGrad = new Gradient();
        gcGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0f), new GradientColorKey(new Color(0.8f, 0.1f, 0f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        gcCol.color = gcGrad;

        // Rising Embers
        embersPS = CreateParticleChild("RisingEmbers", new Vector3(0, 0.1f, 0), pMat, fireRoot.transform);
        var eMain = embersPS.main;
        eMain.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        eMain.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        eMain.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        eMain.simulationSpace = ParticleSystemSimulationSpace.World;
        embersEmission = embersPS.emission;
        embersEmission.rateOverTime = 40f;

        var eShape = embersPS.shape;
        eShape.shapeType = ParticleSystemShapeType.Cone;
        eShape.angle = 14f;
        eShape.radius = 0.12f;
        eShape.rotation = new Vector3(-90f, 0, 0);

        var eCol = embersPS.colorOverLifetime;
        eCol.enabled = true;
        Gradient eGrad = new Gradient();
        eGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.25f, 0f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        eCol.color = eGrad;

        var eNoise = embersPS.noise;
        eNoise.enabled = true;
        eNoise.strength = 0.5f;

        // Billowing Smoke
        smokePS = CreateParticleChild("BillowingSmoke", new Vector3(0, 0.55f, 0), pMat, fireRoot.transform);
        var sMain = smokePS.main;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.55f);
        sMain.simulationSpace = ParticleSystemSimulationSpace.World;
        smokeEmission = smokePS.emission;
        smokeEmission.rateOverTime = 30f;

        var sShape = smokePS.shape;
        sShape.shapeType = ParticleSystemShapeType.Cone;
        sShape.angle = 16f;
        sShape.radius = 0.10f;
        sShape.rotation = new Vector3(-90f, 0, 0);

        var sCol = smokePS.colorOverLifetime;
        sCol.enabled = true;
        Gradient sGrad = new Gradient();
        sGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(0.04f, 0.04f, 0.04f), 0f), new GradientColorKey(new Color(0.12f, 0.12f, 0.13f), 0.6f), new GradientColorKey(new Color(0.22f, 0.22f, 0.22f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.75f, 0.2f), new GradientAlphaKey(0.55f, 0.65f), new GradientAlphaKey(0f, 1f) }
        );
        sCol.color = sGrad;

        var sSize = smokePS.sizeOverLifetime;
        sSize.enabled = true;
        AnimationCurve sCurve = new AnimationCurve();
        sCurve.AddKey(0f, 0.35f);
        sCurve.AddKey(0.4f, 1.1f);
        sCurve.AddKey(1f, 2.3f);
        sSize.size = new ParticleSystem.MinMaxCurve(1f, sCurve);

        // Light
        GameObject fLight = new GameObject("GroundFireLight");
        fLight.transform.SetParent(fireRoot.transform, false);
        fLight.transform.localPosition = new Vector3(0, 0.35f, 0);
        fireLight = fLight.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.52f, 0.15f);
        fireLight.range = 3.6f;
        fireLight.intensity = 2.8f;
    }

    // =========================================================================
    // 4. 3D EXTINGUISHER
    // =========================================================================
    private void Build3DExtinguisher()
    {
        extinguisherRoot = new GameObject("3D_CO2_Extinguisher");
        extinguisherRoot.transform.SetParent(transform, false);

        Shader solidShader = GetCompatibleSolidShader();
        Material redMat = CreateSolidMaterial(solidShader, new Color(0.85f, 0.12f, 0.1f));
        Material blackMat = CreateSolidMaterial(solidShader, new Color(0.15f, 0.15f, 0.16f));
        Material brassMat = CreateSolidMaterial(solidShader, new Color(0.8f, 0.65f, 0.25f));

        CreateSubCylinder(extinguisherRoot.transform, Vector3.zero, new Vector3(0.14f, 0.28f, 0.14f), redMat, Vector3.zero);
        CreateSubCylinder(extinguisherRoot.transform, new Vector3(0, 0.30f, 0), new Vector3(0.05f, 0.04f, 0.05f), brassMat, Vector3.zero);
        CreateSubCube(extinguisherRoot.transform, new Vector3(0, 0.35f, -0.04f), new Vector3(0.02f, 0.08f, 0.12f), blackMat);
        CreateSubCylinder(extinguisherRoot.transform, new Vector3(0.08f, 0.18f, 0.12f), new Vector3(0.06f, 0.15f, 0.06f), blackMat, new Vector3(45f, 0, 0));

        var col = extinguisherRoot.AddComponent<BoxCollider>();
        col.size = new Vector3(0.25f, 0.5f, 0.25f);
        extinguisherRoot.AddComponent<Draggable3DExtinguisher>();

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
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
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
        bannerText.fontSize = 22;
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
        qcRT.sizeDelta = new Vector2(620f, 360f);

        // Title
        GameObject titleObj = new GameObject("QTitle");
        titleObj.transform.SetParent(questionCard.transform, false);
        questionTitleText = titleObj.AddComponent<Text>();
        questionTitleText.font = defaultFont;
        questionTitleText.fontSize = 20;
        questionTitleText.fontStyle = FontStyle.Bold;
        questionTitleText.color = new Color(1f, 0.8f, 0.2f);
        questionTitleText.alignment = TextAnchor.MiddleCenter;
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0, -15f);
        titleRT.sizeDelta = new Vector2(-40f, 35f);

        // Description
        GameObject descObj = new GameObject("QDesc");
        descObj.transform.SetParent(questionCard.transform, false);
        questionDescText = descObj.AddComponent<Text>();
        questionDescText.font = defaultFont;
        questionDescText.fontSize = 15;
        questionDescText.color = new Color(0.9f, 0.9f, 0.9f);
        questionDescText.alignment = TextAnchor.MiddleCenter;
        RectTransform descRT = descObj.GetComponent<RectTransform>();
        descRT.anchorMin = new Vector2(0, 1);
        descRT.anchorMax = new Vector2(1, 1);
        descRT.pivot = new Vector2(0.5f, 1f);
        descRT.anchoredPosition = new Vector2(0, -55f);
        descRT.sizeDelta = new Vector2(-40f, 45f);

        // 3 Option Buttons
        float buttonYStart = -120f;
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
            btnRT.sizeDelta = new Vector2(560f, 55f);
            btnRT.anchoredPosition = new Vector2(0, buttonYStart - i * 65f);

            GameObject btnTextObj = new GameObject("BtnText");
            btnTextObj.transform.SetParent(btnObj.transform, false);
            Text btnText = btnTextObj.AddComponent<Text>();
            btnText.font = defaultFont;
            btnText.fontSize = 15;
            btnText.fontStyle = FontStyle.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAnchor.MiddleCenter;
            optionButtonTexts[i] = btnText;

            RectTransform btTextRT = btnTextObj.GetComponent<RectTransform>();
            btTextRT.anchorMin = Vector2.zero;
            btTextRT.anchorMax = Vector2.one;
            btTextRT.sizeDelta = Vector2.zero;
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
        hudText.fontSize = 14;
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
        sRT.sizeDelta = new Vector2(500f, 260f);

        GameObject sTitleObj = new GameObject("STitle");
        sTitleObj.transform.SetParent(successCard.transform, false);
        Text sTitle = sTitleObj.AddComponent<Text>();
        sTitle.font = defaultFont;
        sTitle.fontSize = 24;
        sTitle.fontStyle = FontStyle.Bold;
        sTitle.color = new Color(0.3f, 1f, 0.45f);
        sTitle.alignment = TextAnchor.MiddleCenter;
        sTitle.text = "TRAINING COMPLETE!";
        RectTransform stRT = sTitleObj.GetComponent<RectTransform>();
        stRT.anchoredPosition = new Vector2(0, 60f);
        stRT.sizeDelta = new Vector2(460f, 40f);

        GameObject sDescObj = new GameObject("SDesc");
        sDescObj.transform.SetParent(successCard.transform, false);
        Text sDesc = sDescObj.AddComponent<Text>();
        sDesc.font = defaultFont;
        sDesc.fontSize = 16;
        sDesc.color = Color.white;
        sDesc.alignment = TextAnchor.MiddleCenter;
        sDesc.text = "You correctly selected insulated PPE, deployed a CO2 extinguisher, aimed at the fuel base, and stopped the electrical fire!";
        RectTransform sdRT = sDescObj.GetComponent<RectTransform>();
        sdRT.anchoredPosition = new Vector2(0, 10f);
        sdRT.sizeDelta = new Vector2(440f, 60f);

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
        rText.fontSize = 16;
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
    // 6. PROCEDURAL ALARM SOUND
    // =========================================================================
    private void BuildAlarmAudio()
    {
        alarmSource = gameObject.AddComponent<AudioSource>();
        alarmSource.clip = GenerateAlarmClip();
        alarmSource.loop = true;
        alarmSource.playOnAwake = false;
        alarmSource.spatialBlend = 0.15f;
        alarmSource.volume = 0.5f;
        alarmSource.Play();
    }

    private AudioClip GenerateAlarmClip()
    {
        int sampleRate = 44100;
        float duration = 1.0f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            bool isBeeping = (t < 0.38f) || (t >= 0.5f && t < 0.88f);
            if (isBeeping)
            {
                float tone = Mathf.Sin(2f * Mathf.PI * 920f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * 1840f * t) * 0.3f;
                samples[i] = Mathf.Clamp(tone, -0.85f, 0.85f) * 0.45f;
            }
            else
            {
                samples[i] = 0f;
            }
        }

        AudioClip clip = AudioClip.Create("FireAlarmAudio", sampleCount, 1, sampleRate, false);
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
            es.AddComponent<StandaloneInputModule>();
        }
    }

    private void EnsurePhysicsRaycaster()
    {
        if (Camera.main != null && Camera.main.GetComponent<PhysicsRaycaster>() == null)
        {
            Camera.main.gameObject.AddComponent<PhysicsRaycaster>();
        }
    }

    // Fixed font fetcher: safely uses non-generic GetBuiltinResource
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
        Color baseCol = new Color(0.72f, 0.72f, 0.73f);
        Color seamCol = new Color(0.48f, 0.48f, 0.50f);

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
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),
                new GradientColorKey(new Color(1f, 0.52f, 0.02f), 0.3f),
                new GradientColorKey(new Color(0.92f, 0.12f, 0f), 0.72f),
                new GradientColorKey(new Color(0.18f, 0.02f, 0.02f), 1f)
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
// DRAGGABLE EXTINGUISHER
// =========================================================================
public class Draggable3DExtinguisher : MonoBehaviour, IDragHandler
{
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        float zDepth = mainCam.WorldToScreenPoint(transform.position).z;
        Vector3 screenPos = new Vector3(eventData.position.x, eventData.position.y, zDepth);
        transform.position = mainCam.ScreenToWorldPoint(screenPos);
    }
}