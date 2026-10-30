using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ashfall;

public static class AshfallBuilder
{
    const string Root="Assets/ASHFALL/";
    [MenuItem("Tools/ASHFALL/Build Game Scene")]
    public static void Build()
    {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        // Refresh the imported OBJ assets before building the scene.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string modelPath = Root + "Models/Player_Body.obj";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null &&
            AssetDatabase.LoadAssetAtPath<Mesh>(modelPath) == null)
        {
            Debug.LogWarning("ASHFALL: The player OBJ could not be loaded as a prefab or mesh. Building the scene with a temporary model instead. Check Assets/ASHFALL/Models/Player_Body.obj.");
        }
        Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        GameObject game=new GameObject("ASHFALL - GAME MANAGER");
        AshfallGame manager=game.AddComponent<AshfallGame>();
        GameObject player=new GameObject("Player - Original Backstab Model");
        player.transform.position=new Vector3(0,1.35f,0);
        CharacterController cc=player.AddComponent<CharacterController>();
        cc.height=1.85f; cc.radius=.32f; cc.center=new Vector3(0,.92f,0); cc.stepOffset=.25f;
        BuildModel("Player",player.transform,true);
        GameObject enemy=new GameObject("Enemy - Original Backstab Model");
        enemy.transform.position=new Vector3(2,2.45f,25.8f);
        BuildModel("Enemy",enemy.transform,false);
        GameObject rig=new GameObject("Camera Rig");
        GameObject cameraObj=new GameObject("Main Camera");
        cameraObj.tag="MainCamera"; cameraObj.transform.SetParent(rig.transform);
        Camera cam=cameraObj.AddComponent<Camera>(); cam.fieldOfView=65; cameraObj.AddComponent<AudioListener>();
        cameraObj.transform.position=new Vector3(0,5,-8); cameraObj.transform.LookAt(new Vector3(0,1,0));
        GameObject sun=new GameObject("Ashen Sun"); Light light=sun.AddComponent<Light>();
        light.type=LightType.Directional; light.color=new Color(1f,.72f,.49f); light.intensity=1.5f;
        sun.transform.rotation=Quaternion.Euler(42,-30,0);
        RenderSettings.ambientLight=new Color(.44f,.38f,.33f);
        manager.player=player.transform; manager.enemy=enemy.transform; manager.cameraRig=rig.transform;
        EditorSceneManager.SaveScene(scene,Root+"Scenes/Ashfall.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Root+"Scenes/Ashfall.unity",true)};
        Selection.activeGameObject=game;
        Debug.Log("ASHFALL scene created! Set Active Input Handling to Both (Edit > Project Settings > Player), then press Play and START GAME.");
    }
    static void BuildModel(string name,Transform parent,bool knife)
    {
        GameObject model=new GameObject("Original "+name+" model"); model.transform.SetParent(parent,false);
        // These are the exact original OBJ character components from Backstab-Game.
        AddPart(name+"_Body",Vector3.zero,model.transform);
        GameObject leftLeg = AddPart(name+"_Leg",new Vector3(-.1f,.92f,0),model.transform);
        GameObject rightLeg = AddPart(name+"_Leg",new Vector3(.1f,.92f,0),model.transform);
        GameObject leftArm = AddPart(name+"_Arm",new Vector3(-.29f,1.4f,0),model.transform);
        GameObject rightArm = AddPart(knife?name+"_ArmKnife":name+"_Arm",new Vector3(.29f,1.4f,0),model.transform);
        HumanoidAnimator animator = model.AddComponent<HumanoidAnimator>();
        animator.legL = leftLeg.transform; animator.legR = rightLeg.transform;
        animator.armL = leftArm.transform; animator.armR = rightArm.transform;
    }
    static GameObject AddPart(string file,Vector3 pos,Transform parent)
    {
        string path = Root + "Models/" + file + ".obj";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject instance = null;
        if (prefab != null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }
        else
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            instance = new GameObject(file);
            MeshFilter filter = instance.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh != null ? mesh : Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (mesh == null) Debug.LogWarning("ASHFALL: Model part not found: " + path + ". Using a temporary fallback.");
        }
        instance.name = file;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = pos;
        ApplyOriginalMaterials(instance, file);
        return instance;
    }
    static void ApplyOriginalMaterials(GameObject instance, string part)
    {
        string manifest = Root + "Models/materials.txt";
        if (!System.IO.File.Exists(manifest)) return;
        string[] names = null;
        foreach (string line in System.IO.File.ReadAllLines(manifest))
        {
            if (line.StartsWith(part + ":")) { names = line.Substring(part.Length + 1).Split(','); break; }
        }
        if (names == null) return;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
        {
            if (renderer.sharedMaterials.Length != names.Length) continue;
            Material[] mats = new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string key = names[i].Trim();
                Color color = PaletteColor(key);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mats[i] = new Material(shader);
                mats[i].name = "Original " + key;
                mats[i].color = color;
            }
            renderer.sharedMaterials = mats;
        }
    }
    static Color PaletteColor(string key)
    {
        switch (key)
        {
            case "Suit": case "Tie": case "Hat": case "Shoe": return new Color(.06f,.06f,.075f);
            case "ShirtW": return new Color(.92f,.92f,.95f);
            case "Skin": return new Color(.85f,.65f,.55f);
            case "Hair": return new Color(.10f,.07f,.05f);
            case "Band": return new Color(.27f,.28f,.30f);
            case "Blade": return new Color(.8f,.84f,.9f);
            case "Pants": return new Color(.37f,.40f,.44f);
            case "ShirtG": return new Color(.55f,.56f,.58f);
            case "Vest": return new Color(.18f,.19f,.21f);
            case "Helmet": return new Color(.29f,.30f,.33f);
            case "Pouch": case "Mask": case "Glove": return new Color(.12f,.13f,.16f);
            case "Boot": return new Color(.08f,.08f,.09f);
            default: return new Color(.12f,.13f,.16f);
        }
    }
}
