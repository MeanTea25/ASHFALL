using System.Collections.Generic;
using UnityEngine;

namespace Ashfall
{
    // Self-contained game manager. Scene geometry is built when the player presses START.
    public class AshfallGame : MonoBehaviour
    {
        public Transform player, enemy, cameraRig;
        public float speed = 6f, jumpSpeed = 9f, gravity = 23f;
        public float backstabRange = 2.3f, behindThreshold = -0.5f;
        CharacterController controller;
        float verticalSpeed, yaw, pitch = 18f, feedbackUntil;
        bool grounded;
        readonly List<Transform> enemies = new List<Transform>();
        readonly HashSet<Transform> defeated = new HashSet<Transform>();
        HumanoidAnimator playerAnimator;
        bool lastGrounded;
        string feedback = "";
        int level, state; // 0 menu, 1 playing, 2 victory
        Vector3 previousPosition, checkpoint;
        Transform levelRoot;
        readonly List<MatrixPlatform> moving = new List<MatrixPlatform>();
        readonly List<Transform> decorative = new List<Transform>();
        readonly List<Renderer> zoneRenderers = new List<Renderer>();
        readonly List<Vector3> platformCenters = new List<Vector3>();
        readonly List<float> platformTops = new List<float>();
        Material zoneRed, zoneGreen;
        Material concrete, metal, rust, glow, sand, black;
        Camera gameCamera;
        GUIStyle titleStyle, bodyStyle, buttonStyle, tinyStyle;
        float lastInputTime;

        void Awake()
        {
            if (player == null || cameraRig == null || enemy == null) { Debug.LogError("ASHFALL: missing player, enemy, or camera rig. Rebuild via Tools > ASHFALL > Build Game Scene."); enabled = false; return; }
            controller = player.GetComponent<CharacterController>();
            playerAnimator = player.GetComponentInChildren<HumanoidAnimator>();
            gameCamera = cameraRig.GetComponentInChildren<Camera>();
            if (controller == null || gameCamera == null) { Debug.LogError("ASHFALL: CharacterController or Camera missing."); enabled = false; return; }
            concrete = Mat(new Color(.29f,.30f,.29f)); metal = Mat(new Color(.13f,.19f,.20f));
            rust = Mat(new Color(.44f,.23f,.13f)); glow = Mat(new Color(.94f,.59f,.19f));
            sand = Mat(new Color(.37f,.31f,.23f)); black = Mat(new Color(.07f,.09f,.10f));
            zoneRed = ZoneMat(new Color(.85f,.12f,.16f)); zoneGreen = ZoneMat(new Color(.09f,.85f,.24f));
            gameCamera.backgroundColor = new Color(.33f,.27f,.23f);
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            player.gameObject.SetActive(false); enemy.gameObject.SetActive(false);
        }
        static Material Mat(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material m = new Material(shader); m.color = color; return m;
        }
        static Material ZoneMat(Color color)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null) shader=Shader.Find("Unlit/Color");
            if(shader==null) shader=Shader.Find("Standard");
            Material m=new Material(shader); m.color=color;
            if(m.HasProperty("_BaseColor")) m.SetColor("_BaseColor",color);
            if(m.HasProperty("_Cull")) m.SetFloat("_Cull",0);
            if(m.HasProperty("_CullMode")) m.SetFloat("_CullMode",0);
            return m;
        }
        void StartLevel(int index)
        {
            if (levelRoot != null) Destroy(levelRoot.gameObject);
            foreach (Transform oldGuard in enemies)
                if (oldGuard != null && oldGuard != enemy) Destroy(oldGuard.gameObject);
            moving.Clear(); decorative.Clear(); zoneRenderers.Clear(); platformCenters.Clear(); platformTops.Clear();
            level = index; state = 1; verticalSpeed = 0;
            enemies.Clear(); defeated.Clear();
            levelRoot = new GameObject("LEVEL " + (level + 1) + " - " + new[]{"THE FALLEN CITY","BROKEN CROSSING","THE LAST OUTPOST"}[level]).transform;
            player.gameObject.SetActive(false);
            enemy.gameObject.SetActive(true);
            // Clear any defeat pose before using the original guard as a clone template.
            HumanoidAnimator originalAnim = enemy.GetComponentInChildren<HumanoidAnimator>();
            if (originalAnim != null) originalAnim.ResetPose();
            Vector3 start = new Vector3(0,1.35f,0);
            player.position = start; player.rotation = Quaternion.identity;
            checkpoint = start; previousPosition = start;
            // Distinct routes: city rooftops, a broken highway, and an elevated outpost.
            // Each route has six jumps and a unique layout/height progression.
            float[] xs = level == 0 ? new float[]{0, 1.3f, -1.0f, 1.6f, -1.3f, 1.0f, 0f}
                       : level == 1 ? new float[]{0, 0, 2.0f, 2.0f, -0.4f, 1.5f, 0f}
                                    : new float[]{0, -1.8f, -1.8f, 1.0f, 2.0f, -0.5f, 0f};
            float[] tops = level == 0 ? new float[]{.65f,1.0f,1.5f,1.15f,1.85f,1.4f,1.8f}
                         : level == 1 ? new float[]{.65f,.65f,1.2f,1.9f,1.25f,2.0f,2.15f}
                                      : new float[]{.65f,1.3f,2.0f,1.4f,2.25f,2.8f,3.0f};
            float[] zs = level == 0 ? new float[]{0,5.0f,10f,15.2f,20.3f,25.3f,30.3f}
                       : level == 1 ? new float[]{0,5.0f,10.1f,15.1f,20.2f,25.2f,30.2f}
                                    : new float[]{0,4.7f,9.5f,14.3f,19.1f,23.9f,28.7f};
            for (int i = 0; i < xs.Length; i++)
            {
                float x=xs[i], z=zs[i], top=tops[i];
                Vector3 center = new Vector3(x, top, z);
                platformCenters.Add(center); platformTops.Add(top);
                float width = i == 0 ? 7f : (level == 1 ? 4.7f : 4.4f);
                GameObject platform = Box((level==0?"Ruined rooftop ":level==1?"Highway slab ":"Outpost walkway ")+i,
                    new Vector3(x,top-.36f,z), new Vector3(width,.72f,level==2?4.3f:4.5f),
                    level==0 ? (i%2==0?concrete:rust) : level==1 ? (i%2==0?metal:concrete) : (i%2==0?metal:rust));
                // Matrix-translated moving platform, placed away from guard spawn locations.
                if (i == 3 && level != 0 || i == 4 && level == 0)
                {
                    MatrixPlatform mp = platform.AddComponent<MatrixPlatform>();
                    mp.origin=platform.transform.position; mp.amplitude=level==2?.8f:1.0f; mp.speed=level==2?.9f:1.1f;
                    moving.Add(mp);
                }
                if (i > 0) Box("Safety edge",new Vector3(x,top+.045f,z-1.9f),new Vector3(width-.4f,.07f,.14f),glow);
                if (level==0)
                {
                    // Rooftops, leaning towers, roof vents and broken parapets.
                    Box("Broken parapet",new Vector3(x-2.0f,top+.45f,z),new Vector3(.18f,.9f,2.1f),rust);
                    if(i%2==0) Box("Roof vent",new Vector3(x+1.5f,top+.24f,z+.8f),new Vector3(.55f,.45f,.6f),metal);
                }
                else if(level==1)
                {
                    // Highway fragments: guardrails, pillars and wrecked road barriers.
                    Box("Road barrier",new Vector3(x+2.0f,top+.32f,z),new Vector3(.18f,.65f,3.5f),rust);
                    Box("Highway support",new Vector3(x,top-2.6f,z),new Vector3(.8f,4.5f,.8f),concrete);
                }
                else
                {
                    // Military outpost: towers, signal lights, cover and steel pillars.
                    Box("Outpost support",new Vector3(x+1.7f,top-1.6f,z),new Vector3(.4f,3.2f,.4f),metal);
                    Box("Signal post",new Vector3(x-1.7f,top+.8f,z),new Vector3(.25f,1.6f,.25f),rust);
                    Box("Warning light",new Vector3(x-1.7f,top+1.65f,z),new Vector3(.48f,.18f,.48f),glow);
                }
            }
            checkpoint = new Vector3(xs[2], tops[2]+.15f, zs[2]);
            Box(level==0?"Dusty city floor":level==1?"Dry riverbed":"Outpost perimeter",
                new Vector3(0,-8.5f,15),new Vector3(110,1,110),sand);
            for (int i=0;i<22;i++)
            {
                float z=(i%11)*4.3f-5f;
                float x=(i/11==0?-1:1)*(11f+(i%4)*3.5f);
                if(level==0) Box("Abandoned skyscraper",new Vector3(x,-2.4f,z),new Vector3(3.5f,Random.Range(9f,22f),4.2f),i%3==0?rust:concrete);
                else if(level==1)
                {
                    Box("Collapsed overpass column",new Vector3(x,-4.0f,z),new Vector3(1.5f,Random.Range(4f,9f),1.5f),concrete);
                    Box("Broken highway segment",new Vector3(x,-.5f,z),new Vector3(5f,.8f,2.5f),metal);
                }
                else
                {
                    Box("Outpost perimeter tower",new Vector3(x,-2f,z),new Vector3(2.2f,Random.Range(8f,15f),2.2f),metal);
                    Box("Barricade",new Vector3(x+2,-.8f,z+1),new Vector3(3.0f,2.2f,.55f),rust);
                }
            }
            Vector3 exitPos = new Vector3(xs[6],tops[6]+1.9f,zs[6]+.6f);
            GameObject exit=Box(level==2?"EVACUATION GATE":"NEXT DISTRICT",exitPos,new Vector3(3.6f,3.8f,.5f),black);
            Box("Exit marker",exitPos+Vector3.up*2.0f,new Vector3(3.6f,.22f,.65f),glow);
            AshfallExit trigger=exit.AddComponent<AshfallExit>(); trigger.game=this;
            exit.GetComponent<BoxCollider>().isTrigger=true;
            // Every district requires defeating its guards before the gate unlocks.
            // The original enemy model is cloned for additional guards.
            int count=level+1;
            // Guard spawns always use actual platform coordinates, not hardcoded positions.
            int[] guardPlatforms = level==0 ? new int[]{5} : level==1 ? new int[]{2,5} : new int[]{1,4,5};
            for(int n=0;n<count;n++)
            {
                Transform guard = n==0 ? enemy : Instantiate(enemy.gameObject).transform;
                guard.name="Wasteland Guard "+(n+1);
                guard.gameObject.SetActive(true);
                // Remove zones inherited from the original guard when cloning.
                foreach(Transform child in guard.GetComponentsInChildren<Transform>(true))
                    if(child!=guard && child.name=="Backstab Zone") Destroy(child.gameObject);
                int platformIndex = guardPlatforms[n];
                Vector3 spot = platformCenters[platformIndex];
                // Center on stationary platform; keep the final guard slightly off-center.
                guard.position=new Vector3(spot.x,spot.y+2f,spot.z+(n%2==0?.2f:-.3f));
                guard.rotation=Quaternion.Euler(0, n%2==0?0:180,0);
                HumanoidAnimator guardAnim=guard.GetComponentInChildren<HumanoidAnimator>();
                if(guardAnim!=null) guardAnim.ResetPose();
                // Align model feet with the platform's top using the model's world bounds.
                float lowest=float.PositiveInfinity;
                foreach(Renderer r in guard.GetComponentsInChildren<Renderer>())
                    if(r.enabled && r.bounds.min.y<lowest) lowest=r.bounds.min.y;
                if(!float.IsInfinity(lowest)) guard.position+=Vector3.up*(spot.y-lowest+.025f);
                CreateBackstabZone(guard,spot.y);
                enemies.Add(guard);
            }
            player.gameObject.SetActive(true);
            if (playerAnimator != null) playerAnimator.ResetPose();
            yaw = 0; pitch = 18;
            feedback = "DEFEAT ALL GUARDS, THEN REACH THE EXIT"; feedbackUntil = Time.time + 2.5f;
        }
        GameObject Box(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name; obj.transform.SetParent(levelRoot);
            obj.transform.position = pos; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = mat;
            return obj;
        }
        void Update()
        {
            if (state != 1) return;
            UpdateZones();
            float dt = Time.deltaTime;
            previousPosition = player.position;
            float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
            if (Input.GetKey(KeyCode.Q)) yaw -= 90f*dt;
            if (Input.GetKey(KeyCode.E)) yaw += 90f*dt;
            if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X")*3f; pitch = Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2f,-5f,65f); }
            Vector3 movement = Quaternion.Euler(0,yaw,0)*new Vector3(h,0,v);
            movement = Vector3.ClampMagnitude(movement,1);
            if (movement.sqrMagnitude > .01f) player.rotation = Quaternion.RotateTowards(player.rotation,Quaternion.LookRotation(movement),720f*dt);
            grounded = controller.isGrounded;
            if (grounded && verticalSpeed < 0) verticalSpeed = -1.8f;
            if (grounded && Input.GetKeyDown(KeyCode.Space))
            {
                verticalSpeed = jumpSpeed;
                if (playerAnimator != null) playerAnimator.PlayJump();
            }
            verticalSpeed -= gravity*dt; // manual Euler integration of gravity
            controller.Move((movement*speed*(Input.GetKey(KeyCode.LeftShift)?1.35f:1f)+Vector3.up*verticalSpeed)*dt);
            if (playerAnimator != null && controller.isGrounded && !lastGrounded) playerAnimator.PlayLand();
            lastGrounded = controller.isGrounded;
            if (player.position.y < -6f) Respawn();
            if (Input.GetKeyDown(KeyCode.F)) Attack();
            if (Input.GetKeyDown(KeyCode.R)) Respawn();
            if (Input.GetKeyDown(KeyCode.Escape)) { state=0; player.gameObject.SetActive(false); enemy.gameObject.SetActive(false); }
        }
        void LateUpdate()
        {
            if (state!=1 || gameCamera == null) return;
            Vector3 target = player.position + Vector3.up*1.3f;
            Vector3 offset = Quaternion.Euler(pitch,yaw,0)*new Vector3(0,2.8f,-7.6f);
            gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position,target+offset,Mathf.Clamp01(Time.deltaTime*8));
            gameCamera.transform.LookAt(target);
        }
        void Respawn()
        {
            controller.enabled=false; player.position=checkpoint; controller.enabled=true;
            verticalSpeed=0; feedback="CHECKPOINT RESTORED"; feedbackUntil=Time.time+1.5f;
        }
        void Attack()
        {
            Transform target = null;
            float nearest = float.MaxValue;
            foreach (Transform guard in enemies)
            {
                if (guard == null || defeated.Contains(guard)) continue;
                Vector3 delta = player.position - guard.position; delta.y = 0;
                if (delta.magnitude < nearest) { nearest = delta.magnitude; target = guard; }
            }
            if (target == null) { Feedback("ALL GUARDS DEFEATED - REACH THE GATE"); return; }
            Vector3 toPlayer = player.position - target.position; toPlayer.y = 0;
            float dist = toPlayer.magnitude;
            float dot = dist > .001f ? Vector3.Dot(target.forward, toPlayer.normalized) : 1f;
            if (playerAnimator != null) playerAnimator.PlayStab();
            if (dist <= backstabRange && dot < behindThreshold)
            {
                defeated.Add(target);
                HumanoidAnimator anim = target.GetComponentInChildren<HumanoidAnimator>();
                if (anim != null) anim.PlayDeath();
                else target.gameObject.SetActive(false);
                foreach (Renderer zone in target.GetComponentsInChildren<Renderer>())
                    if (zone.gameObject.name == "Backstab Zone") zone.enabled = false;
                Feedback("BACKSTAB SUCCESSFUL  |  " + defeated.Count + "/" + enemies.Count + " GUARDS");
            }
            else Feedback("ATTACK FAILED  |  dot=" + dot.ToString("0.00") + "  distance=" + dist.ToString("0.0"));
        }
        Transform NearestGuard(out float distance, out float dot)
        {
            Transform nearest = null;
            distance = float.PositiveInfinity; dot = 1f;
            foreach (Transform guard in enemies)
            {
                if (guard == null || defeated.Contains(guard)) continue;
                Vector3 offset = player.position - guard.position; offset.y = 0f;
                float d = offset.magnitude;
                if (d < distance)
                {
                    nearest = guard; distance = d;
                    dot = d > .001f ? Vector3.Dot(guard.forward, offset / d) : 1f;
                }
            }
            return nearest;
        }
        void CreateBackstabZone(Transform guard,float platformY)
        {
            // Local fan points behind the guard (negative local Z).
            const int segments = 30;
            float radius = backstabRange;
            var vertices = new Vector3[segments + 2];
            // Double-sided triangles avoid invisible zones caused by mesh winding/culling.
            var triangles = new int[segments * 6];
            vertices[0] = Vector3.zero;
            for(int i=0;i<=segments;i++)
            {
                float angle=Mathf.Lerp(-60f,60f,i/(float)segments)*Mathf.Deg2Rad;
                vertices[i+1]=new Vector3(Mathf.Sin(angle)*radius,0,-Mathf.Cos(angle)*radius);
                if(i<segments)
                {
                    int t=i*6;
                    triangles[t]=0; triangles[t+1]=i+1; triangles[t+2]=i+2;
                    triangles[t+3]=0; triangles[t+4]=i+2; triangles[t+5]=i+1;
                }
            }
            Mesh mesh = new Mesh { name = "Backstab indicator fan" };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals();
            GameObject fan = new GameObject("Backstab Zone");
            fan.transform.SetParent(guard, false);
            // Ground indicator sits just above the platform, not at the character pivot.
            fan.transform.position=new Vector3(guard.position.x,platformY+.09f,guard.position.z);
            fan.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = fan.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = zoneRed;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            zoneRenderers.Add(renderer);
        }
        void UpdateZones()
        {
            float d, dot;
            Transform near = NearestGuard(out d, out dot);
            for (int i = 0; i < enemies.Count; i++)
            {
                Transform guard = enemies[i];
                if (guard == null) continue;
                foreach (Renderer r in guard.GetComponentsInChildren<Renderer>())
                {
                    if (r.gameObject.name != "Backstab Zone") continue;
                    r.enabled = !defeated.Contains(guard);
                    r.sharedMaterial = guard == near && d <= backstabRange && dot < behindThreshold ? zoneGreen : zoneRed;
                }
            }
        }
        void Feedback(string msg) { feedback=msg; feedbackUntil=Time.time+2.7f; }
        public void EnterExit()
        {
            if(state!=1) return;
            if(defeated.Count < enemies.Count) { Feedback("DEFEAT ALL GUARDS FIRST (F)  " + defeated.Count + "/" + enemies.Count); return; }
            if(level==2) { state=2; player.gameObject.SetActive(false); }
            else StartLevel(level+1);
        }
        void Styles()
        {
            if(titleStyle!=null) return;
            titleStyle=new GUIStyle(GUI.skin.label){fontSize=38,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter}; titleStyle.normal.textColor=Color.white;
            bodyStyle=new GUIStyle(GUI.skin.label){fontSize=18,alignment=TextAnchor.MiddleCenter,wordWrap=true}; bodyStyle.normal.textColor=Color.white;
            tinyStyle=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true}; tinyStyle.normal.textColor=Color.white;
            buttonStyle=new GUIStyle(GUI.skin.button){fontSize=22,fontStyle=FontStyle.Bold};
        }
        void OnGUI()
        {
            Styles(); float w=Screen.width,h=Screen.height;
            if(state==0 || state==2)
            {
                GUI.Box(new Rect(w*.24f,h*.18f,w*.52f,h*.64f),"");
                GUI.Label(new Rect(w*.25f,h*.24f,w*.5f,60),"ASHFALL",titleStyle);
                GUI.Label(new Rect(w*.27f,h*.36f,w*.46f,100),state==0?"THE LAST ESCAPE\nThree districts. One way out.\nWASD move  |  Space jump  |  Shift sprint\nQ/E orbit  |  F backstab  |  R checkpoint":"YOU ESCAPED THE WASTELAND!\nAll three levels completed.",bodyStyle);
                if(GUI.Button(new Rect(w*.36f,h*.62f,w*.28f,52),state==0?"START GAME":"PLAY AGAIN",buttonStyle)) StartLevel(0);
                return;
            }
            GUI.Box(new Rect(12,12,410,228),"");
            GUI.Label(new Rect(22,18,340,24),"LEVEL "+(level+1)+" / 3 - "+new[]{"FALLEN CITY","BROKEN CROSSING","LAST OUTPOST"}[level],tinyStyle);
            GUI.Label(new Rect(22,47,345,24),"Previous: "+previousPosition.ToString("F1"),tinyStyle);
            GUI.Label(new Rect(22,75,345,24),"Current:  "+player.position.ToString("F1"),tinyStyle);
            GUI.Label(new Rect(22,102,345,24),"Grounded: "+controller.isGrounded+"   |   Guards: "+defeated.Count+"/"+enemies.Count+"   |   F: backstab",tinyStyle);
            float debugDistance, debugDot;
            Transform debugGuard = NearestGuard(out debugDistance, out debugDot);
            string position = debugGuard == null ? "NONE" : (debugDot < behindThreshold ? "BEHIND" : (debugDot > .5f ? "FRONT" : "SIDE"));
            GUI.Label(new Rect(22,132,390,24),"BACKSTAB DEBUG  |  Target: " + (debugGuard == null ? "none" : debugGuard.name),tinyStyle);
            GUI.Label(new Rect(22,156,390,24),"Dot product: " + (debugGuard == null ? "--" : debugDot.ToString("F2")) + "  (behind if < " + behindThreshold.ToString("F2") + ")",tinyStyle);
            GUI.Label(new Rect(22,180,390,24),"Distance: " + (debugGuard == null ? "--" : debugDistance.ToString("F1")) + " m  |  Range: " + backstabRange.ToString("F1") + " m",tinyStyle);
            GUI.Label(new Rect(22,204,390,24),"Player is: " + position + "  |  Backstab ready: " + (debugGuard != null && debugDistance <= backstabRange && debugDot < behindThreshold ? "YES" : "NO"),tinyStyle);
            if(Time.time<feedbackUntil) { GUI.Box(new Rect(w*.30f,20,w*.4f,48),""); GUI.Label(new Rect(w*.31f,28,w*.38f,32),feedback,bodyStyle); }
            GUI.Label(new Rect(w*.25f,h-35,w*.5f,26),"WASD MOVE   SPACE JUMP   SHIFT SPRINT   Q/E CAMERA   F ATTACK   R RESPAWN",tinyStyle);
        }
    }
    public class AshfallExit : MonoBehaviour
    {
        public AshfallGame game;
        void OnTriggerEnter(Collider other) { if(other.GetComponent<CharacterController>()!=null && game!=null) game.EnterExit(); }
    }
    public class MatrixPlatform : MonoBehaviour
    {
        public Vector3 origin;
        public float amplitude=1.5f, speed=1f;
        void Update()
        {
            // Matrix4x4.TRS explicitly transforms the platform's origin each frame.
            Matrix4x4 matrix=Matrix4x4.TRS(new Vector3(Mathf.Sin(Time.time*speed)*amplitude,0,0),Quaternion.identity,Vector3.one);
            transform.position=matrix.MultiplyPoint3x4(origin);
        }
    }
}
