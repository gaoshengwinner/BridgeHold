using System;
using System.Collections.Generic;
using UnityEngine;

namespace BridgeHold
{
    // One central simulation: no per-enemy physics, Update, or pathfinding.
    public sealed partial class BridgeHoldGame : MonoBehaviour
    {
        enum Element { Ice, Fire, Shock }
        enum WeaponKind { Pea, TriplePea }
        enum EnemyKind { Walker, Medic, Boss }
        enum RunState { Playing, Paused, Won, Lost }
        sealed class Enemy
        {
            public Transform root, body, head;
            public Renderer skin;
            public EnemyKind kind;
            public float hp, maxHp, speed, attackAt, healAt, slowUntil, burnUntil, burnTick, flashUntil;
            public int burnLevel;
            public float phase, slowMultiplier=1;
            public bool alive;
        }
        sealed class Pea { public Transform root; public Vector3 direction; public float remaining, damage; public Element element; public int elementLevel, rewardGeneration; public bool center; }
        readonly List<Pea> peas = new List<Pea>(120);
        int rewardGeneration;
        readonly Color peaColor = new Color(.48f, 1f, .12f);
        sealed class Shell { public Transform root; public Vector3 start, end; public float age; public int level; }
        sealed class Fx { public Transform root; public LineRenderer line; public TextMesh label; public float life, duration; public Vector3 velocity; public bool expand; }
        const int MaxEnemies = 300;
        readonly List<Enemy> enemies = new List<Enemy>(MaxEnemies);
        readonly List<Shell> shells = new List<Shell>(24);
        readonly List<Fx> effects = new List<Fx>(160);
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly int[] elementLevels = { 1, 1, 1 };
        readonly int[] turretLevels = { 1, 1 };
        readonly float[] turretTimers = new float[2];
        readonly Transform[] turrets = new Transform[2];
        readonly Enemy[] turretTargets = new Enemy[2];
        readonly List<GameObject> wallParts = new List<GameObject>();
        readonly List<Rect> uiRects = new List<Rect>();
        Camera cam;
        Transform wall, player, gun, muzzle, crate;
        Renderer crateRenderer;
        readonly Transform[] weaponModels = new Transform[2];
        readonly Renderer[] elementPlates = new Renderer[3];
        readonly List<Renderer> wallSurfaces = new List<Renderer>();
        Transform wallElementFxRoot;
        readonly Transform[] wallElementFxGroups = new Transform[3];
        readonly Transform[] iceCrystals = new Transform[15];
        readonly Transform[] wallFlames = new Transform[16];
        LineRenderer wallElectricArc;
        float wallElementPulse=.9f;
        WeaponKind activeWeapon;
        float ShotDamage => damage;
        float ShotRate => fireRate * (activeWeapon == WeaponKind.TriplePea ? .65f : 1f);
        RunState state;
        Element activeElement;
        int wallLevel = 1, points = 40, wave, kills, spawnRemaining, spawnedThisWave, waveTotal, weaponLevel = 1;
        float wallHp = 450, playerHp = 100, nextSpawn, waveDelay = 2, fireTimer, crateTimer = 10, crateHp;
        float damage = 24, fireRate = 6, shake, elapsed;
        bool crateAlive, bossSpawned;
        int rewardIndex, rewardPoints, rewardSide=-1, rewardHits;
        bool rewardActive;
        float rewardPop;
        readonly Transform[] rewardBlocks=new Transform[2];
        Vector3 cameraHome;
        GUIStyle label, small, title, button, tiny;
        Texture2D white;
        AudioSource audioSource;
        AudioClip shotSound, boomSound;
        float guiScale;
        readonly Color navy = new Color(.035f, .09f, .16f, .95f);
        readonly Color gold = new Color(1, .74f, .16f);
        readonly Color[] elementColors = { new Color(.15f,.83f,1), new Color(1,.3f,.08f), new Color(.8f,.26f,1) };
        float WallMax => 450 + (wallLevel - 1) * 300;
        int AliveCount { get { int n = 0; foreach (var e in enemies) if (e.alive) n++; return n; } }

        void Awake()
        {
            colorBlock = new MaterialPropertyBlock();
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            QualitySettings.shadowDistance = 48;
            QualitySettings.shadows=ShadowQuality.All; QualitySettings.shadowResolution=ShadowResolution.Medium;
            QualitySettings.antiAliasing=4;
            QualitySettings.vSyncCount = 0;
            BuildScene();
            BuildPools();
            audioSource = gameObject.AddComponent<AudioSource>();
            shotSound = Tone(180, .045f);
            boomSound = Tone(65, .16f);
            state = RunState.Playing;
            RefreshElementVisuals();
        }
        Material Mat(string name, Color color)
        {
            if (materials.TryGetValue(name, out var existing)) return existing;
            var m = new Material(Resources.Load<Material>("BridgeSurface"));
            m.name = name; m.color = color; m.SetFloat("_Glossiness", .32f); m.enableInstancing = true;
            materials.Add(name, m); return m;
        }
        Transform Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            var go = type==PrimitiveType.Cube?new GameObject(name):GameObject.CreatePrimitive(type); go.name = name;
            if(type==PrimitiveType.Cube) { go.AddComponent<MeshFilter>().sharedMesh=SoftCubeMesh(); go.AddComponent<MeshRenderer>(); }
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if(name=="Body" || name=="Head" || name=="Helmet" || name=="Pillar" || name=="Cliff" || name=="Continuous wall" || name=="Tree crown")
                go.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            Destroy(go.GetComponent<Collider>()); return go.transform;
        }
        Transform Group(string name, Transform parent = null)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        void BuildScene()
        {
            var environment=Group("Static bridge environment");
            RenderSettings.ambientLight = new Color(.48f,.56f,.69f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(.42f,.72f,.85f);
            RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 45; RenderSettings.fogEndDistance = 95;
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.7f; light.color=new Color(1,.91f,.76f); light.shadows=LightShadows.Soft; light.shadowStrength=.65f; light.shadowBias=.04f; light.transform.rotation = Quaternion.Euler(48, -30, 0);
            cam = new GameObject("Portrait Camera").AddComponent<Camera>(); cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor;
            cameraHome = new Vector3(0, 22, -22); cam.transform.position = cameraHome;
            cam.transform.LookAt(new Vector3(0, 0, 9)); cam.fieldOfView = 41; cam.farClipPlane = 130;
            cam.gameObject.AddComponent<AudioListener>();
            var stone = Mat("warm sandstone", new Color(.68f,.49f,.31f));
            var tile = Mat("bridge paving", new Color(.84f,.65f,.40f));
            var edge = Mat("edge stone", new Color(.49f,.39f,.32f));
            var wood = Mat("timber", new Color(.48f,.23f,.075f));
            var blue = Mat("royal blue", new Color(.035f,.24f,.84f));
            var yellow = Mat("brass", gold);
            Shape("River", PrimitiveType.Cube, new Vector3(0,-2,22), new Vector3(110,.4f,140), Mat("water",new Color(.045f,.52f,.84f)));
            Shape("Bridge deck", PrimitiveType.Cube, new Vector3(0,-.5f,17), new Vector3(12,1,64), stone);
            // Broad shared mesh paving strips keep the environment inexpensive.
            for (int z = -13; z < 48; z += 2)
                for (int x = -5; x < 6; x += 2)
                {
                    int shade=Mathf.Abs(x*17+z*31)%5;
                    Shape("Paving",PrimitiveType.Cube,new Vector3(x+((z+13)%4==0?.12f:-.12f),.025f,z),new Vector3(1.91f,.15f,1.91f),Mat("paver "+shade,Color.Lerp(new Color(.63f,.45f,.29f),new Color(.88f,.72f,.48f),shade/4f)));
                }
            AddScenicDetails(stone,wood,blue,yellow);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape("Bridge parapet",PrimitiveType.Cube,new Vector3(side*6,.6f,17),new Vector3(.6f,1.2f,64),edge);
                Shape("Rail cap",PrimitiveType.Cube,new Vector3(side*6,1.25f,17),new Vector3(.8f,.22f,64),stone);
                for (int z = -10; z < 49; z += 7)
                {
                    Shape("Pillar",PrimitiveType.Cube,new Vector3(side*6,1.05f,z),new Vector3(1,2.1f,1),stone);
                    Shape("Brazier",PrimitiveType.Cylinder,new Vector3(side*6,2.3f,z),new Vector3(.9f,.16f,.9f),wood);
                    Shape("Flame",PrimitiveType.Sphere,new Vector3(side*6,2.85f,z),new Vector3(.38f,1,.38f),yellow);
                    if (z % 2 == 0)
                        Shape("Blue banner",PrimitiveType.Cube,new Vector3(side*5.42f,.85f,z),new Vector3(.08f,1.4f,.85f),blue);
                }
                for (int z = -4; z < 61; z += 6)
                {
                    float x = side * (12 + Mathf.Abs(Mathf.Sin(z)) * 6);
                    Shape("Cliff",PrimitiveType.Cube,new Vector3(x,-1,z),new Vector3(8,5,5.8f),stone);
                    Shape("Grass",PrimitiveType.Cube,new Vector3(x,1.6f,z),new Vector3(8,.4f,5.8f),Mat("grass",new Color(.29f,.57f,.14f)));
                    Shape("Trunk",PrimitiveType.Cylinder,new Vector3(x,3,z),new Vector3(.45f,1.3f,.45f),wood);
                    for(int i=0;i<3;i++) Shape("Tree crown",PrimitiveType.Sphere,new Vector3(x,4+i*.8f,z),new Vector3(3-i*.6f,2,3-i*.6f),Mat("pine",new Color(.16f,.43f,.17f)));
                }
            }
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                renderer.transform.SetParent(environment,true);
            StaticBatchingUtility.Combine(environment.gameObject);
            wall = Group("One continuous defense wall"); wall.position = new Vector3(0,0,4);
            BuildWall();
            for (int i=0;i<2;i++)
            {
                var t = Group(i==0?"Left mortar":"Right mortar"); t.position=new Vector3(i==0?-4.4f:4.4f,2.4f,4);
                Shape("Mount",PrimitiveType.Cylinder,Vector3.zero,new Vector3(1.5f,.35f,1.5f),yellow,t);
                turrets[i]=Group("Aiming barrel",t);
                var barrel = Shape("Mortar",PrimitiveType.Cylinder,new Vector3(0,.6f,.25f),new Vector3(.8f,.9f,.8f),Mat("gunmetal",new Color(.1f,.17f,.27f)),turrets[i]);
                barrel.localRotation=Quaternion.Euler(48,0,0);
                Shape("Breech",PrimitiveType.Sphere,new Vector3(0,.3f,0),Vector3.one*.85f,yellow,t);
                AddCannonDetails(t,turrets[i],yellow);
            }
            player=Group("Player"); player.position=new Vector3(0,0,-3.5f);
            Shape("Boot left",PrimitiveType.Cube,new Vector3(-.3f,.25f,0),new Vector3(.42f,.5f,.7f),wood,player);
            Shape("Boot right",PrimitiveType.Cube,new Vector3(.3f,.25f,0),new Vector3(.42f,.5f,.7f),wood,player);
            Shape("Jacket",PrimitiveType.Capsule,new Vector3(0,1,0),new Vector3(1.15f,.65f,.8f),blue,player);
            Shape("Head",PrimitiveType.Sphere,new Vector3(0,1.8f,0),Vector3.one*.85f,Mat("skin",new Color(1,.75f,.46f)),player);
            Shape("Helmet",PrimitiveType.Sphere,new Vector3(0,2.1f,-.05f),new Vector3(1.25f,.85f,1.15f),blue,player);
            Shape("Backpack",PrimitiveType.Cube,new Vector3(0,1.1f,-.55f),new Vector3(.9f,.85f,.4f),wood,player);
            AddPlayerDetails(blue,yellow,wood);
            gun=Group("Weapon",player); gun.localPosition=new Vector3(.55f,1.2f,.25f);
            var peaGreen=Mat("pea gun",new Color(.28f,.7f,.12f));
            var peaDark=Mat("pea barrel opening",new Color(.055f,.18f,.025f));
            for(int i=0;i<2;i++)
            {
                weaponModels[i]=Group(i==0?"Pea shooter":"Triple pea shooter",gun);
                Shape("Leaf grip",PrimitiveType.Capsule,new Vector3(0,-.12f,.3f),new Vector3(.32f,.4f,.45f),peaGreen,weaponModels[i]);
                int barrels=i==0?1:3;
                for(int j=0;j<barrels;j++)
                {
                    float x=(j-(barrels-1)*.5f)*.32f;
                    Shape("Pea chamber",PrimitiveType.Sphere,new Vector3(x,0,.65f),new Vector3(.42f,.44f,.85f),peaGreen,weaponModels[i]);
                    var tube=Shape("Pea muzzle",PrimitiveType.Cylinder,new Vector3(x,0,1.15f),new Vector3(.32f,.24f,.32f),peaGreen,weaponModels[i]);
                    tube.localRotation=Quaternion.Euler(90,0,0);
                    var opening=Shape("Muzzle opening",PrimitiveType.Cylinder,new Vector3(x,0,1.395f),new Vector3(.23f,.01f,.23f),peaDark,weaponModels[i]);
                    opening.localRotation=Quaternion.Euler(90,0,0);
                }
                weaponModels[i].gameObject.SetActive(i==0);
            }
            muzzle=Group("Muzzle",gun); muzzle.localPosition=new Vector3(0,0,1.4f);
            crate=Group("Shootable reward crate"); crate.position=new Vector3(2.8f,1.1f,15);
            crateRenderer=Shape("Crate",PrimitiveType.Cube,Vector3.zero,Vector3.one*1.6f,yellow,crate).GetComponent<Renderer>();
            Shape("Band",PrimitiveType.Cube,Vector3.zero,new Vector3(1.65f,.35f,1.65f),blue,crate);
            crate.gameObject.SetActive(false);
            for(int i=0;i<2;i++)
            {
                rewardBlocks[i]=Group(i==0?"Shoot to upgrade weapon":"Shoot for defense points");
                var material=i==0?Mat("weapon reward",elementColors[0]):yellow;
                Shape("Reward block",PrimitiveType.Cube,Vector3.zero,Vector3.one*1.6f,material,rewardBlocks[i]);
                Shape("Reward stripe",PrimitiveType.Cube,Vector3.zero,new Vector3(1.65f,.25f,1.65f),blue,rewardBlocks[i]);
                rewardBlocks[i].gameObject.SetActive(false);
            }
        }
        void BuildWall()
        {
            foreach(var p in wallParts) Destroy(p); wallParts.Clear(); wallSurfaces.Clear();
            Color[] colors={new Color(.55f,.28f,.09f),new Color(.58f,.62f,.65f),new Color(.26f,.36f,.46f),new Color(.78f,.4f,.2f),gold};
            var m=Mat("wall tier "+wallLevel,colors[wallLevel-1]);
            var core=Shape("Continuous wall",PrimitiveType.Cube,new Vector3(0,1.05f,0),new Vector3(11.6f,2.1f,.65f+wallLevel*.15f),m,wall);
            wallParts.Add(core.gameObject);
            int segments=wallLevel==1?16:10;
            for(int i=0;i<segments;i++)
            {
                var p=Shape("Wall detail",PrimitiveType.Cube,new Vector3(-5.4f+i*10.8f/(segments-1),1.2f,-.55f),new Vector3(wallLevel==1?.52f:1,.0f+1.9f,.18f),m,wall);
                wallParts.Add(p.gameObject);
                if(wallLevel>=3)
                {
                    var rivet=Shape("Armor rivet",PrimitiveType.Sphere,new Vector3(p.localPosition.x,1.7f,-.72f),Vector3.one*.17f,Mat("brass",gold),wall);
                    wallParts.Add(rivet.gameObject);
                }
            }
            var cap=Shape("Wall crown",PrimitiveType.Cube,new Vector3(0,2.1f,0),new Vector3(11.9f,.22f,1.1f+wallLevel*.1f),m,wall); wallParts.Add(cap.gameObject);
            AddWallDetails();
            foreach(var part in wallParts) wallSurfaces.Add(part.GetComponent<Renderer>());
            if(!wallElementFxRoot) BuildWallElementEffects();
            for(int i=0;i<3;i++)
            {
                var mount=Shape("Wall element mount",PrimitiveType.Cube,new Vector3((i-1)*3.4f,1.12f,-.8f),new Vector3(2.8f,1.55f,.22f),Mat("element frame",navy),wall);
                wallParts.Add(mount.gameObject);
                var plate=Shape("Player-facing element plate",PrimitiveType.Cube,new Vector3((i-1)*3.4f,1.12f,-.94f),new Vector3(2.6f,1.35f,.09f),Mat("element plate",Color.white),wall);
                elementPlates[i]=plate.GetComponent<Renderer>(); wallParts.Add(plate.gameObject);
            }
            RefreshElementVisuals();
        }
        Material[] skinTints;
        void BuildPools()
        {
            skinTints=new[]{Mat("zombie normal",new Color(.64f,.77f,.23f)),Mat("zombie hit",Color.white),Mat("zombie burning",elementColors[1]),Mat("zombie frozen",elementColors[0])};
            var skin=Mat("zombie skin",new Color(.64f,.77f,.23f));
            var red=Mat("zombie shirt",new Color(.74f,.13f,.09f));
            var dark=Mat("eyes",new Color(.035f,.045f,.055f));
            var eye=Mat("glowing eyes",new Color(1,1,.25f));
            for(int i=0;i<MaxEnemies;i++)
            {
                var e=new Enemy(); e.root=Group("Pooled zombie "+i); e.phase=UnityEngine.Random.value*6;
                e.body=Shape("Body",PrimitiveType.Capsule,new Vector3(0,.65f,0),new Vector3(.65f,.45f,.45f),red,e.root);
                e.head=Shape("Head",PrimitiveType.Sphere,new Vector3(0,1.35f,0),new Vector3(.86f,.8f,.72f),skin,e.root); e.skin=e.head.GetComponent<Renderer>();
                for(int side=-1;side<=1;side+=2)
                {
                    Shape("Eye socket",PrimitiveType.Sphere,new Vector3(side*.2f,1.4f,-.32f),Vector3.one*.22f,dark,e.root);
                    Shape("Eye",PrimitiveType.Sphere,new Vector3(side*.2f,1.42f,-.42f),Vector3.one*.09f,eye,e.root);
                }
                Shape("Mouth",PrimitiveType.Cube,new Vector3(0,1.1f,-.34f),new Vector3(.3f,.16f,.09f),dark,e.root);
                AddZombieDetails(e.root,i,skin,red,dark);
                e.root.gameObject.SetActive(false); enemies.Add(e);
            }
            for(int i=0;i<120;i++)
            {
                var pea=new Pea { root=Shape("Pooled pea",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.32f,Mat("pea projectile",peaColor)) };
                pea.root.gameObject.SetActive(false); peas.Add(pea);
            }
            var fxMat=Mat("effect",Color.white);
            var lineMat=Resources.Load<Material>("BridgeTrail");
            for(int i=0;i<140;i++)
            {
                var f=new Fx(); f.root=Shape("Pooled effect",PrimitiveType.Sphere,Vector3.zero,Vector3.one,fxMat);
                f.line=f.root.gameObject.AddComponent<LineRenderer>(); f.line.sharedMaterial=lineMat;
                f.line.positionCount=2; f.line.startWidth=.07f; f.line.endWidth=.04f; f.line.useWorldSpace=true;
                var textObject=new GameObject("Damage"); textObject.transform.SetParent(f.root,false);
                f.label=textObject.AddComponent<TextMesh>();
                f.label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                f.label.GetComponent<Renderer>().sharedMaterial=f.label.font.material;
                f.label.fontSize=40; f.label.characterSize=.12f; f.label.anchor=TextAnchor.MiddleCenter;
                f.root.gameObject.SetActive(false); effects.Add(f);
            }
            for(int i=0;i<20;i++)
            {
                var s=new Shell(); s.root=Shape("Pooled mortar shell",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.35f,Mat("shell",gold));
                s.root.gameObject.SetActive(false); shells.Add(s);
            }
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            if(state!=RunState.Playing) return;
            float dt=Mathf.Min(Time.deltaTime,.05f); elapsed+=dt;
            UpdateWaves(dt); if(state!=RunState.Playing) return;
            UpdateEnemies(dt); if(state!=RunState.Playing) return;
            UpdateWeapon(dt); if(state!=RunState.Playing) return;
            UpdatePeas(dt); UpdateTurrets(dt); UpdateEffects(dt); UpdateWallElementEffects(dt);
            if(state!=RunState.Playing) return;
            if(!rewardActive) crateTimer-=dt;
            if(rewardActive)
            {
                rewardPop=Mathf.Min(1,rewardPop+dt*5);
                foreach(var block in rewardBlocks) block.localScale=Vector3.one*Mathf.Lerp(.15f,1,rewardPop);
            }
            if(!crateAlive && !rewardActive && crateTimer<=0) { crateAlive=true; crateHp=90+wave*15; crate.gameObject.SetActive(true); crate.position=new Vector3(UnityEngine.Random.value>.5f?3:-3,1.15f,UnityEngine.Random.Range(12,19)); }
            if(crateAlive) { crate.Rotate(0,dt*24,0); crate.position=new Vector3(crate.position.x,1.25f+Mathf.Sin(elapsed*2)*.15f,crate.position.z); }
            shake=Mathf.Max(0,shake-dt*1.5f); cam.transform.position=cameraHome+UnityEngine.Random.insideUnitSphere*shake;
        }
        void UpdateWaves(float dt)
        {
            if(spawnRemaining==0 && AliveCount==0)
            {
                if(wave>=10) { state=RunState.Won; return; }
                waveDelay-=dt; if(waveDelay>0) return;
                wave++; waveTotal=20+wave*12; spawnRemaining=waveTotal; spawnedThisWave=0; bossSpawned=false; waveDelay=4;
                points+=10;
            }
            nextSpawn-=dt;
            if(spawnRemaining>0 && nextSpawn<=0 && AliveCount<MaxEnemies)
            {
                int batch=Mathf.Min(4+wave/2,spawnRemaining);
                for(int i=0;i<batch;i++)
                {
                    EnemyKind kind=(wave>=2 && spawnedThisWave%17==12)?EnemyKind.Medic:EnemyKind.Walker;
                    if((wave==3||wave==6||wave==10) && !bossSpawned && spawnRemaining<=waveTotal/2) { kind=EnemyKind.Boss; bossSpawned=true; }
                    Spawn(kind); spawnRemaining--; spawnedThisWave++;
                }
                nextSpawn=.8f;
            }
        }
        void Spawn(EnemyKind kind)
        {
            foreach(var e in enemies)
            {
                if(e.alive) continue;
                e.alive=true; e.kind=kind;
                e.maxHp=(kind==EnemyKind.Boss?1100:kind==EnemyKind.Medic?130:65)*(1+wave*.12f); e.hp=e.maxHp;
                e.speed=kind==EnemyKind.Boss?.72f:1.05f+wave*.045f;
                e.slowUntil=e.burnUntil=e.burnTick=e.flashUntil=0; e.attackAt=elapsed+.5f; e.healAt=elapsed+3;
                e.root.position=new Vector3(UnityEngine.Random.Range(-4.8f,4.8f),0,UnityEngine.Random.Range(35f,43f));
                e.root.localScale=Vector3.one*(kind==EnemyKind.Boss?2.6f:1);
                e.body.GetComponent<Renderer>().sharedMaterial=Mat(kind==EnemyKind.Medic?"medic coat":kind==EnemyKind.Boss?"boss armor":"zombie shirt",kind==EnemyKind.Medic?Color.white:kind==EnemyKind.Boss?new Color(.53f,.14f,.38f):new Color(.74f,.13f,.09f));
                e.root.gameObject.SetActive(true); return;
            }
        }
        void UpdateEnemies(float dt)
        {
            foreach(var e in enemies)
            {
                if(!e.alive) continue;
                if(e.burnUntil>elapsed && e.burnTick<=elapsed) { e.burnTick=elapsed+.5f; Hurt(e,5+e.burnLevel*4,elementColors[1]); if(!e.alive) continue; }
                if(wallHp<=0)
                {
                    Vector3 position=e.root.position;
                    float slow=e.slowUntil>elapsed?e.slowMultiplier:1;
                    position.x=Mathf.MoveTowards(position.x,player.position.x,e.speed*slow*dt);
                    e.root.position=position;
                }
                float stop=wallHp>0?5.5f+(e.kind==EnemyKind.Boss?.8f:0):player.position.z+1.25f;
                if(e.root.position.z>stop)
                {
                    float slow=e.slowUntil>elapsed?e.slowMultiplier:1;
                    e.root.position+=Vector3.back*e.speed*slow*dt;
                }
                else if(e.attackAt<=elapsed && (wallHp>0 || Mathf.Abs(e.root.position.x-player.position.x)<=1.2f))
                {
                    e.attackAt=elapsed+1;
                    float hit=e.kind==EnemyKind.Boss?65:6;
                    if(wallHp>0)
                    {
                        wallHp=Mathf.Max(0,wallHp-hit/(1+(wallLevel-1)*.18f));
                        if(wallHp==0) { wall.gameObject.SetActive(false); foreach(var t in turrets) t.parent.gameObject.SetActive(false); shake=.35f; }
                    }
                    else { playerHp=Mathf.Max(0,playerHp-hit); shake=.16f; if(playerHp<=0) { state=RunState.Lost; return; } }
                }
                if(e.kind==EnemyKind.Medic && e.healAt<=elapsed)
                {
                    e.healAt=elapsed+3;
                    foreach(var other in enemies) if(other.alive && (other.root.position-e.root.position).sqrMagnitude<25) other.hp=Mathf.Min(other.maxHp,other.hp+18);
                    Burst(e.root.position+Vector3.up, new Color(.25f,1,.45f), 2);
                }
                e.body.localRotation=Quaternion.Euler(0,0,Mathf.Sin(elapsed*7+e.phase)*7);
                int tint=e.flashUntil>elapsed?1:e.burnUntil>elapsed?2:e.slowUntil>elapsed?3:0;
                if(e.skin.sharedMaterial!=skinTints[tint]) e.skin.sharedMaterial=skinTints[tint];
            }
        }
        MaterialPropertyBlock colorBlock;
        void SetTint(Renderer r, Color c) { colorBlock.SetColor("_Color",c); r.SetPropertyBlock(colorBlock); }
        const float PlayerHalfWidth = 4.8f;
        bool combatDrag;
        int combatFinger = -1;
        Vector2 previousPointer;
        void MovePlayer(float distance)
        {
            if(state!=RunState.Playing) return;
            Vector3 position=player.position;
            position.x=Mathf.Clamp(position.x+distance,-PlayerHalfWidth,PlayerHalfWidth);
            player.position=position;
        }
        bool PointerOverUI(Vector2 screen)
        {
            float scale=Mathf.Min(Screen.width/540f,Screen.height/960f);
            Vector2 gui=new Vector2((screen.x-(Screen.width-540*scale)*.5f)/scale,(Screen.height-screen.y)/scale);
            for(int i=0;i<2;i++) if(WeaponCardRect(i).Contains(gui)) return true;
            foreach(var rect in uiRects) if(rect.Contains(gui)) return true;
            return false;
        }
        void BuildWallElementEffects()
        {
            wallElementFxRoot=Group("Wall elemental effects",wall);
            for(int i=0;i<3;i++) wallElementFxGroups[i]=Group(((Element)i)+" wall effect",wallElementFxRoot);
            var ice=Mat("wall ice glow",new Color(.35f,.9f,1));
            var fire=Mat("wall flame glow",new Color(1,.38f,.045f));
            ice.EnableKeyword("_EMISSION"); ice.SetColor("_EmissionColor",elementColors[0]*.7f);
            fire.EnableKeyword("_EMISSION"); fire.SetColor("_EmissionColor",elementColors[1]);
            for(int i=0;i<iceCrystals.Length;i++)
            {
                iceCrystals[i]=Shape("Frost crystal",PrimitiveType.Cube,new Vector3(-5.3f+i*.76f,2.24f,-.63f),new Vector3(.16f,.32f,.16f),ice,wallElementFxGroups[0]);
                iceCrystals[i].localRotation=Quaternion.Euler(0,0,i%2==0?24:-24);
            }
            for(int i=0;i<wallFlames.Length;i++)
                wallFlames[i]=Shape("Wall flame",PrimitiveType.Sphere,new Vector3(-5.3f+i*.71f,2.36f,-.63f),new Vector3(.2f,.5f,.2f),fire,wallElementFxGroups[1]);
            wallElectricArc=wallElementFxGroups[2].gameObject.AddComponent<LineRenderer>();
            wallElectricArc.sharedMaterial=Resources.Load<Material>("BridgeTrail");
            wallElectricArc.useWorldSpace=false; wallElectricArc.positionCount=25;
            wallElectricArc.startWidth=wallElectricArc.endWidth=.065f;
            wallElectricArc.startColor=wallElectricArc.endColor=elementColors[2];
        }
        void UpdateWallElementEffects(float dt)
        {
            if(!wallElementFxRoot) return;
            wallElementFxRoot.gameObject.SetActive(wallHp>0);
            if(wallHp<=0) return;
            wallElementPulse=Mathf.Max(0,wallElementPulse-dt);
            float pulse=wallElementPulse/.9f;
            Color elementColor=elementColors[(int)activeElement];
            foreach(var surface in wallSurfaces) if(surface)
                SetTint(surface,Color.Lerp(surface.sharedMaterial.color,elementColor,.16f+pulse*.32f));
            for(int i=0;i<3;i++) wallElementFxGroups[i].gameObject.SetActive(i==(int)activeElement);
            if(activeElement==Element.Ice)
            {
                for(int i=0;i<iceCrystals.Length;i++)
                {
                    float shimmer=.5f+.5f*Mathf.Sin(elapsed*2+i*.9f);
                    iceCrystals[i].localScale=new Vector3(.14f,.25f+shimmer*.12f+pulse*.28f,.14f);
                }
            }
            else if(activeElement==Element.Fire)
            {
                for(int i=0;i<wallFlames.Length;i++)
                {
                    float flicker=.5f+.5f*Mathf.Sin(elapsed*11+i*2.3f);
                    wallFlames[i].localPosition=new Vector3(-5.3f+i*.71f,2.3f+flicker*.15f,-.63f);
                    wallFlames[i].localScale=new Vector3(.18f+flicker*.08f,.24f+flicker*.3f+pulse*.35f,.18f);
                }
            }
            else
            {
                float tick=Mathf.Floor(elapsed*13);
                wallElectricArc.startWidth=wallElectricArc.endWidth=.045f+pulse*.05f;
                for(int i=0;i<25;i++)
                    wallElectricArc.SetPosition(i,new Vector3(-5.4f+i*.45f,2.3f+Mathf.Sin(i*5.7f+tick*8.3f)*(.08f+pulse*.12f),-.75f));
                wallElectricArc.startColor=wallElectricArc.endColor=Color.Lerp(elementColors[2],Color.white,.25f+pulse*.5f);
            }
        }
        void SelectElement(int index)
        {
            if(state!=RunState.Playing || wallHp<=0 || index<0 || index>=3 || (int)activeElement==index) return;
            activeElement=(Element)index;
            wallElementPulse=.9f;
            RefreshElementVisuals();
        }
        void RefreshElementVisuals()
        {
            UpdateWallElementEffects(0);
            for(int i=0;i<3;i++) if(elementPlates[i])
                SetTint(elementPlates[i],i==(int)activeElement?elementColors[i]:elementColors[i]*.32f);
            foreach(var model in weaponModels) if(model)
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                    if(renderer.gameObject.name!="Muzzle opening") SetTint(renderer,elementColors[(int)activeElement]);
        }
        void SelectWeapon(int index)
        {
            if(state!=RunState.Playing || index<0 || index>=weaponModels.Length || (int)activeWeapon==index) return;
            activeWeapon=(WeaponKind)index;
            for(int i=0;i<weaponModels.Length;i++) weaponModels[i].gameObject.SetActive(i==(int)activeWeapon);
            // Keep the previous shot's cooldown: switching cannot create free shots.
        }
        Rect WeaponCardRect(int index)
        {
            float scale=Mathf.Min(Screen.width/540f,Screen.height/960f);
            float bottom=(Screen.height-Screen.safeArea.yMin)/scale-8;
            return new Rect(100+index*176,bottom-76,164,76);
        }
        void UpdateWeapon(float dt)
        {
            fireTimer-=dt;
            // Process all fingers before the combat finger, supporting two-thumb play.
            foreach(var touch in Input.touches)
            {
                float scale=Mathf.Min(Screen.width/540f,Screen.height/960f);
                Vector2 point=new Vector2((touch.position.x-(Screen.width-540*scale)*.5f)/scale,(Screen.height-touch.position.y)/scale);
                if(touch.phase==TouchPhase.Began)
                    for(int i=0;i<2;i++) if(WeaponCardRect(i).Contains(point)) SelectWeapon(i);
            }
            float horizontal=(Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)
                -(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0);
            MovePlayer(horizontal*7*dt);
            Vector2 screen=Input.mousePosition;
            bool held=false;
            if(Input.touchCount>0)
            {
                foreach(var touch in Input.touches)
                {
                    if(touch.phase==TouchPhase.Began && combatFinger<0 && !PointerOverUI(touch.position))
                    { combatFinger=touch.fingerId; combatDrag=true; previousPointer=touch.position; }
                    if(touch.fingerId!=combatFinger) continue;
                    screen=touch.position;
                    held=touch.phase!=TouchPhase.Ended && touch.phase!=TouchPhase.Canceled;
                    if(!held) { combatDrag=false; combatFinger=-1; }
                    break;
                }
            }
            else
            {
                if(combatFinger>=0) { combatFinger=-1; combatDrag=false; }
                if(Input.GetMouseButtonDown(0))
                { combatDrag=!PointerOverUI(screen); previousPointer=screen; }
                held=Input.GetMouseButton(0);
                if(!held) combatDrag=false;
            }
            if(held && combatDrag)
            {
                bool blocked=PointerOverUI(screen);
                if(!blocked) MovePlayer((screen.x-previousPointer.x)/Screen.width*(PlayerHalfWidth*2));
                previousPointer=screen;
                if(blocked) return;
                if(fireTimer<=0) { fireTimer=1/ShotRate; Shoot(); }
            }
        }
        void Shoot()
        {
            gun.rotation=Quaternion.identity;
            int count=activeWeapon==WeaponKind.TriplePea?3:1;
            for(int i=0;i<count;i++)
            {
                Pea pea=null;
                foreach(var candidate in peas) if(!candidate.root.gameObject.activeSelf) { pea=candidate; break; }
                if(pea==null) break;
                int lane=count==1?0:i-1;
                pea.root.position=muzzle.position+Vector3.right*(lane*.32f);
                pea.direction=new Vector3(lane*.065f,0,1).normalized;
                pea.remaining=65; pea.element=activeElement; pea.elementLevel=elementLevels[(int)activeElement];
                pea.damage=ShotDamage*(activeElement==Element.Fire?1.5f+(pea.elementLevel-1)*.2f:1);
                SetTint(pea.root.GetComponent<Renderer>(),elementColors[(int)pea.element]);
                pea.center=lane==0; pea.rewardGeneration=rewardActive?rewardGeneration:-1;
                pea.root.gameObject.SetActive(true);
            }
            if(audioSource) audioSource.PlayOneShot(shotSound,.12f);
        }
        void UpdatePeas(float dt)
        {
            foreach(var pea in peas)
            {
                if(!pea.root.gameObject.activeSelf) continue;
                Vector3 start=pea.root.position, dir=pea.direction;
                float distance=Mathf.Min(pea.remaining,32*dt), closest=distance+.001f;
                Enemy target=null; bool box=false; int choice=-1;
                foreach(var e in enemies)
                {
                    if(!e.alive) continue;
                    Vector3 delta=e.root.position-start; delta.y=0;
                    float along=Vector3.Dot(delta,dir), radius=e.kind==EnemyKind.Boss?1.36f:.66f;
                    if(along>=0 && along<closest && (delta-dir*along).sqrMagnitude<radius*radius)
                    { closest=along; target=e; }
                }
                if(crateAlive)
                {
                    Vector3 delta=crate.position-start; delta.y=0; float along=Vector3.Dot(delta,dir);
                    if(along>=0 && along<closest && (delta-dir*along).sqrMagnitude<1.16f*1.16f)
                    { closest=along; target=null; box=true; }
                }
                if(rewardActive)
                {
                    for(int i=0;i<2;i++)
                    {
                        Vector3 delta=rewardBlocks[i].position-start; delta.y=0; float along=Vector3.Dot(delta,dir);
                        if(along>=0 && along<closest && (delta-dir*along).sqrMagnitude<1.06f*1.06f)
                        { closest=along; target=null; box=false; choice=i; }
                    }
                }
                bool hit=target!=null || box || choice>=0;
                pea.root.position=start+dir*(hit?closest:distance); pea.remaining-=distance;
                if(!hit && pea.remaining>0) continue;
                pea.root.gameObject.SetActive(false);
                // Only the middle pea counts toward reward selection, once per trigger.
                if(pea.center && rewardActive && pea.rewardGeneration==rewardGeneration)
                {
                    if(choice>=0) HitReward(choice);
                    else { rewardSide=-1; rewardHits=0; }
                }
                if(!hit) continue;
                Burst(pea.root.position,elementColors[(int)pea.element],.22f);
                if(box)
                {
                    crateHp-=pea.damage; Number(crate.position+Vector3.up,pea.damage,gold);
                    if(crateHp<=0) OpenRewards();
                }
                else if(target!=null)
                {
                    Color c=elementColors[(int)pea.element]; Hurt(target,pea.damage,c);
                    if(pea.element==Element.Ice)
                    { target.slowUntil=elapsed+2.5f; target.slowMultiplier=Mathf.Max(.2f,.65f-pea.elementLevel*.06f); }
                    if(pea.element==Element.Shock) Chain(target,pea.damage,pea.elementLevel);
                }
            }
        }
        void OpenRewards()
        {
            crateAlive=false; crate.gameObject.SetActive(false);
            rewardGeneration++;
            rewardActive=true; rewardSide=-1; rewardHits=0; rewardPop=0;
            rewardIndex=(weaponLevel-1)%2; rewardPoints=40+wave*5;
            for(int i=0;i<2;i++)
            {
                rewardBlocks[i].position=new Vector3(i==0?-3:3,1.25f,crate.position.z);
                rewardBlocks[i].localScale=Vector3.one*.15f;
                rewardBlocks[i].gameObject.SetActive(true);
            }
        }
        void HitReward(int side)
        {
            if(!rewardActive) return;
            rewardHits=rewardSide==side?rewardHits+1:1; rewardSide=side;
            Burst(rewardBlocks[side].position,side==0?elementColors[0]:gold,.5f);
            if(rewardHits<3) return;
            if(side==0)
            {
                if(rewardIndex==0) damage*=1.25f; else fireRate=Mathf.Min(16,fireRate*1.2f);
                weaponLevel++;
            }
            else points+=rewardPoints;
            rewardActive=false; rewardSide=-1; rewardHits=0; crateTimer=17;
            foreach(var block in rewardBlocks) block.gameObject.SetActive(false);
        }
        readonly HashSet<Enemy> chained=new HashSet<Enemy>();
        void Chain(Enemy first,float shotDamage,int level)
        {
            chained.Clear(); chained.Add(first); Enemy current=first;
            for(int i=0;i<1+level;i++)
            {
                Enemy best=null; float nearest=Mathf.Pow(2.5f+level*.3f,2);
                foreach(var e in enemies)
                {
                    if(!e.alive || chained.Contains(e)) continue;
                    float d=(e.root.position-current.root.position).sqrMagnitude;
                    if(d<nearest) { nearest=d; best=e; }
                }
                if(best==null) break;
                Trace(current.root.position+Vector3.up,best.root.position+Vector3.up,elementColors[2],.22f);
                Hurt(best,shotDamage*(.3f+level*.08f),elementColors[2]); chained.Add(best); current=best;
            }
        }
        void Hurt(Enemy e,float amount,Color color)
        {
            if(!e.alive) return;
            e.hp-=amount; e.flashUntil=elapsed+.08f;
            Number(e.root.position+Vector3.up*(e.kind==EnemyKind.Boss?3.8f:1.9f),amount,color);
            if(e.hp<=0)
            {
                e.alive=false; e.root.gameObject.SetActive(false); kills++;
                for(int i=0;i<2;i++) if(turretTargets[i]==e) turretTargets[i]=null;
                if(e.kind==EnemyKind.Boss) { Burst(e.root.position+Vector3.up,gold,3); shake=.3f; points+=20; }
            }
        }
        Enemy SelectTurretTarget(int index)
        {
            Enemy other=turretTargets[1-index];
            Enemy best=null;
            bool bestOnSide=false;
            float nearest=float.PositiveInfinity;
            foreach(var enemy in enemies)
            {
                if(!enemy.alive || enemy==other) continue;
                bool onSide=index==0?enemy.root.position.x<=0:enemy.root.position.x>=0;
                float distance=enemy.root.position.z;
                if(best==null || (onSide && !bestOnSide) || (onSide==bestOnSide && distance<nearest))
                { best=enemy; bestOnSide=onSide; nearest=distance; }
            }
            // Share a target only when there is no other living enemy to attack.
            return best ?? (other!=null && other.alive?other:null);
        }
        void UpdateTurrets(float dt)
        {
            if(wallHp>0)
            for(int i=0;i<2;i++)
            {
                turretTimers[i]-=dt; if(turretTimers[i]>0) continue;
                Enemy target=SelectTurretTarget(i);
                if(target==null) continue;
                foreach(var s in shells)
                {
                    if(s.root.gameObject.activeSelf) continue;
                    turretTargets[i]=target;
                    Vector3 direction=target.root.position-turrets[i].position; direction.y=0;
                    turrets[i].rotation=Quaternion.LookRotation(direction);
                    s.start=turrets[i].position+Vector3.up; s.end=target.root.position+Vector3.back*.6f; s.age=0; s.level=turretLevels[i];
                    s.root.position=s.start; s.root.gameObject.SetActive(true);
                    turretTimers[i]=3.3f/(1+turretLevels[i]*.15f); break;
                }
            }
            foreach(var s in shells)
            {
                if(!s.root.gameObject.activeSelf) continue;
                s.age+=dt; float t=s.age/1.1f;
                s.root.position=Vector3.Lerp(s.start,s.end,t)+Vector3.up*Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI)*6;
                if(t<1) continue;
                s.root.gameObject.SetActive(false); float radius=1.7f+s.level*.3f; Burst(s.end+Vector3.up*.3f,gold,radius); audioSource.PlayOneShot(boomSound,.2f);
                foreach(var e in enemies) if(e.alive && (e.root.position-s.end).sqrMagnitude<radius*radius) { Hurt(e,25+s.level*15,gold); if(e.alive) e.root.position+=Vector3.forward*.35f; }
            }
        }
        Fx TakeFx(float duration)
        {
            foreach(var f in effects)
            {
                if(f.life>0) continue;
                f.life=f.duration=duration; f.root.gameObject.SetActive(true); f.root.localScale=Vector3.one;
                f.root.GetComponent<Renderer>().enabled=false; f.line.enabled=false; f.label.gameObject.SetActive(false); f.velocity=Vector3.zero; f.expand=false;
                return f;
            }
            return null;
        }
        void Trace(Vector3 a,Vector3 b,Color c,float duration)
        {
            var f=TakeFx(duration); if(f==null)return; f.line.enabled=true;
            f.line.SetPosition(0,a); f.line.SetPosition(1,b); f.line.startColor=f.line.endColor=c;
        }
        void Burst(Vector3 p,Color c,float size)
        {
            var f=TakeFx(.28f); if(f==null)return; f.root.position=p; f.root.localScale=Vector3.one*size;
            var r=f.root.GetComponent<Renderer>(); r.enabled=true; SetTint(r,c); f.expand=true;
        }
        void Number(Vector3 p,float number,Color c)
        {
            var f=TakeFx(.55f); if(f==null)return; f.root.position=p; f.label.gameObject.SetActive(true);
            f.label.text=Mathf.CeilToInt(number).ToString(); f.label.color=c; f.velocity=Vector3.up*1.5f;
            f.label.transform.rotation=cam.transform.rotation;
        }
        void UpdateEffects(float dt)
        {
            foreach(var f in effects)
            {
                if(f.life<=0) continue; f.life-=dt;
                if(f.life<=0) { f.root.gameObject.SetActive(false); continue; }
                f.root.position+=f.velocity*dt;
                if(f.expand) f.root.localScale*=1+dt*2;
            }
        }
        AudioClip Tone(float frequency,float duration)
        {
            int count=(int)(22050*duration); float[] data=new float[count];
            for(int i=0;i<count;i++) data[i]=Mathf.Sin(i*frequency*2*Mathf.PI/22050)*(1-i/(float)count)*.4f;
            var clip=AudioClip.Create("Synthesized feedback",count,1,22050,false); clip.SetData(data,0); return clip;
        }
        void TogglePause() { combatDrag=false; combatFinger=-1; if(state==RunState.Playing) state=RunState.Paused; else if(state==RunState.Paused) state=RunState.Playing; }
        void Restart()
        {
            foreach(var e in enemies) { e.alive=false; e.root.gameObject.SetActive(false); }
            foreach(var f in effects) { f.life=0; f.root.gameObject.SetActive(false); }
            foreach(var s in shells) s.root.gameObject.SetActive(false);
            foreach(var pea in peas) pea.root.gameObject.SetActive(false);
            combatDrag=false; combatFinger=-1; player.position=new Vector3(0,0,-3.5f);
            wallLevel=1; wallHp=450; playerHp=100; points=40; wave=kills=spawnRemaining=0; waveDelay=2; nextSpawn=0;
            rewardActive=false; rewardSide=-1; rewardHits=0;
            foreach(var block in rewardBlocks) block.gameObject.SetActive(false);
            weaponLevel=1; damage=24; fireRate=6; elapsed=0; crateAlive=false; crateTimer=10; crate.gameObject.SetActive(false);
            activeWeapon=WeaponKind.Pea; fireTimer=0;
            for(int i=0;i<weaponModels.Length;i++) weaponModels[i].gameObject.SetActive(i==0);
            activeElement=Element.Ice; wallElementPulse=.9f; for(int i=0;i<3;i++) elementLevels[i]=1;
            for(int i=0;i<2;i++) { turretLevels[i]=1; turretTimers[i]=0; turretTargets[i]=null; turrets[i].localRotation=Quaternion.identity; turrets[i].parent.gameObject.SetActive(true); turrets[i].localScale=Vector3.one; }
            wall.gameObject.SetActive(true); BuildWall(); state=RunState.Playing;
        }
        void OnApplicationPause(bool pause) { if(pause) { combatDrag=false; combatFinger=-1; if(state==RunState.Playing) state=RunState.Paused; } }
        void OnGUI()
        {
            InitGui(); guiScale=Mathf.Min(Screen.width/540f,Screen.height/960f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-540*guiScale)*.5f,0,0),Quaternion.identity,Vector3.one*guiScale);
            uiRects.Clear();
            float top=Mathf.Max(12,(Screen.height-Screen.safeArea.yMax)/guiScale+8);
            CardPanel(new Rect(14,top,512,68),navy);
            if(Btn(new Rect(23,top+12,42,42),state==RunState.Paused?">":"II")) TogglePause();
            Text(new Rect(78,top+5,245,29),"WAVE "+Mathf.Max(1,wave)+" / 10",title);
            Text(new Rect(78,top+36,210,20),kills+" ELIMINATED",small);
            Text(new Rect(340,top+8,175,24),"DEFENSE POINTS",tiny);
            Text(new Rect(355,top+27,153,32),points.ToString()+" DP",title,gold);
            float progress=waveTotal>0?1-(spawnRemaining+AliveCount)/(float)waveTotal:0;
            Bar(new Rect(15,top+72,510,6),progress,gold);
            if(wallHp>0)
            {
                Vector2 wp=Project(new Vector3(0,2.3f,4));
                Panel(new Rect(wp.x-76,wp.y-49,152,45),navy);
                Text(new Rect(wp.x-71,wp.y-46,106,20),"WALL  Lv."+wallLevel,small);
                Upgrade(new Rect(wp.x+37,wp.y-46,34,30),wallLevel,()=>{wallLevel++;wallHp=Mathf.Min(WallMax,wallHp+420);BuildWall();});
                Bar(new Rect(wp.x-69,wp.y-17,138,7),wallHp/WallMax,new Color(.3f,.95f,.48f));
                for(int i=0;i<2;i++)
                {
                    int index=i; Vector2 p=Project(turrets[i].position+Vector3.up*1.7f);
                    Panel(new Rect(p.x-50,p.y-27,100,49),navy);
                    Text(new Rect(p.x-45,p.y-25,68,18),"GUN Lv."+turretLevels[i],tiny);
                    Upgrade(new Rect(p.x+12,p.y-23,34,30),turretLevels[i],()=>{turretLevels[index]++;turrets[index].localScale=Vector3.one*(1+(turretLevels[index]-1)*.07f);});
                    Text(new Rect(p.x-44,p.y-4,62,18),Cost(turretLevels[i])+" DP",tiny,gold);
                }
            }
            else
            {
                Vector2 wp=Project(new Vector3(0,1,4));
                Panel(new Rect(wp.x-125,wp.y-20,250,32),navy); Text(new Rect(wp.x-120,wp.y-18,240,26),"WALL LOST • HOLD THE BRIDGE",small,new Color(1,.3f,.15f));
            }
            // Element plaques are attached to the player-facing wall surface.
            string[] names={"ICE","FIRE","SHOCK"};
            for(int i=0;i<3 && wallHp>0;i++)
            {
                int index=i; Vector2 p=Project(wall.TransformPoint(new Vector3((i-1)*3.4f,1.12f,-1f)));
                Color c=elementColors[i]; Rect r=new Rect(p.x-46,p.y-38,92,64);
                if(SelectButton(new Rect(r.x+17,r.y-4,58,46),"",c)) SelectElement(i);
                DrawElementBadge(new Rect(r.x+20,r.y-4,52,44),i,activeElement==(Element)i);
                CardPanel(new Rect(r.x,r.y+42,92,24),navy);
                Text(new Rect(r.x+2,r.y+44,56,20),"L"+elementLevels[i]+" "+(elementLevels[i]<5?Cost(elementLevels[i])+"DP":"MAX"),tiny,activeElement==(Element)i?navy:Color.white);
                Upgrade(new Rect(r.x+61,r.y+43,28,21),elementLevels[i],()=>elementLevels[index]++);
            }
            if(crateAlive)
            {
                Vector2 p=Project(crate.position+Vector3.up*1.5f);
                Text(new Rect(p.x-75,p.y-24,150,22),"SHOOT FOR REWARD",tiny,gold);
                Bar(new Rect(p.x-37,p.y,74,6),crateHp/(90+wave*15),gold);
            }
            foreach(var e in enemies) if(e.alive && e.kind==EnemyKind.Boss)
            {
                Vector2 p=Project(e.root.position+Vector3.up*4.3f);
                Text(new Rect(p.x-70,p.y-24,140,22),"BRIDGE BREAKER",tiny,new Color(1,.35f,.25f));
                Bar(new Rect(p.x-65,p.y,130,8),e.hp/e.maxHp,new Color(1,.2f,.2f));
            }
            float bottom=(Screen.height-Screen.safeArea.yMin)/guiScale-8;
            Bar(new Rect(100,bottom-94,340,12),playerHp/100,new Color(.96f,.18f,.21f));
            Text(new Rect(100,bottom-98,340,20),Mathf.CeilToInt(playerHp)+" / 100  •  Lv."+weaponLevel+"  •  "+names[(int)activeElement],tiny);
            bool guiEnabled=GUI.enabled; GUI.enabled=guiEnabled && state==RunState.Playing;
            for(int i=0;i<2;i++)
            {
                Rect card=WeaponCardRect(i); bool selected=(int)activeWeapon==i;
                if(SelectButton(card,"",Color.white) && Input.touchCount==0) SelectWeapon(i);
                CardPanel(new Rect(card.x+2,card.y+2,card.width-4,card.height-4),selected?new Color(.1f,.25f,.12f,.98f):navy);
                Panel(new Rect(card.x+2,card.y+2,card.width-4,3),selected?peaColor:new Color(.3f,.4f,.45f));
                // Small gun silhouette makes each persistent card recognizable at a glance.
                int barrels=i==0?1:3;
                for(int j=0;j<barrels;j++)
                    Panel(new Rect(card.x+18+j*9,card.y+17,7,24),peaColor);
                Panel(new Rect(card.x+18,card.y+35,barrels==1?12:25,10),peaColor);
                Panel(new Rect(card.x+20,card.y+43,7,10),peaColor);
                Text(new Rect(card.x+50,card.y+11,110,22),i==0?"PEA":"TRIPLE PEA",small);
                Text(new Rect(card.x+50,card.y+34,110,18),i==0?"1 PEA / SHOT":"3 PEAS / SHOT",tiny,peaColor);
                Text(new Rect(card.x+4,card.y+55,card.width-8,18),selected?"EQUIPPED":"TAP TO EQUIP",tiny,selected?peaColor:Color.white);
            }
            GUI.enabled=guiEnabled;
            if(wave==0) Text(new Rect(50,150,440,45),"DEFEND THE BRIDGE",title,gold);
            if(rewardActive)
            {
                for(int i=0;i<2;i++)
                {
                    Vector2 p=Project(rewardBlocks[i].position+Vector3.up*1.5f);
                    Panel(new Rect(p.x-73,p.y-42,146,60),navy);
                    Text(new Rect(p.x-71,p.y-40,142,20),i==0?"WEAPON":"DEFENSE",small,i==0?elementColors[0]:gold);
                    Text(new Rect(p.x-71,p.y-20,142,18),i==0?(rewardIndex==0?"Damage +25%":"Fire rate +20%"):"+"+rewardPoints+" DP",tiny);
                    Text(new Rect(p.x-71,p.y-2,142,18),"HIT "+(rewardSide==i?rewardHits:0)+" / 3",tiny,gold);
                }
            }
            if(state==RunState.Paused || state==RunState.Lost || state==RunState.Won)
            {
                Panel(new Rect(0,0,540,960),new Color(.02f,.04f,.08f,.87f));
                Text(new Rect(35,330,470,58),state==RunState.Paused?"PAUSED":state==RunState.Won?"BRIDGE SECURED":"BRIDGE OVERRUN",title,gold);
                Text(new Rect(50,393,440,30),"Wave "+wave+"  •  "+kills+" eliminated",label);
                if(Btn(new Rect(155,460,230,55),state==RunState.Paused?"RESUME":"PLAY AGAIN")) { if(state==RunState.Paused) state=RunState.Playing; else Restart(); }
            }
            GUI.matrix=Matrix4x4.identity;
        }
        int Cost(int level) => 20+level*15;
        void Upgrade(Rect r,int level,Action apply)
        {
            bool allowed=points>=Cost(level) && level<5 && state==RunState.Playing;
            bool prior=GUI.enabled; GUI.enabled=prior&&allowed;
            if(Btn(r,level>=5?"•":"+",allowed?new Color(.22f,.92f,.35f):Color.gray)) { points-=Cost(level); apply(); }
            GUI.enabled=prior;
        }
        Vector2 Project(Vector3 p)
        {
            Vector3 s=cam.WorldToScreenPoint(p);
            return new Vector2((s.x-(Screen.width-540*guiScale)*.5f)/guiScale,(Screen.height-s.y)/guiScale);
        }
        void InitGui()
        {
            if(label!=null) return;
            white=Texture2D.whiteTexture;
            label=new GUIStyle(GUI.skin.label){fontSize=19,alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold}; label.normal.textColor=Color.white;
            small=new GUIStyle(label){fontSize=15}; tiny=new GUIStyle(label){fontSize=11}; title=new GUIStyle(label){fontSize=25};
            button=new GUIStyle(GUI.skin.button){fontSize=16,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        }
        void Panel(Rect r,Color c) { Color old=GUI.color; GUI.color=c; GUI.DrawTexture(r,white); GUI.color=old; }
        void Text(Rect r,string text,GUIStyle style,Color? color=null) { Color old=GUI.color; GUI.color=color??Color.white; GUI.Label(r,text,style); GUI.color=old; }
        bool SelectButton(Rect r,string text,Color color)
        {
            uiRects.Add(r); Color old=GUI.color; GUI.color=color;
            bool pressed=GUI.RepeatButton(r,text,button); GUI.color=old;
            if(pressed && Input.touchCount==0) combatDrag=false;
            return pressed;
        }
        bool Btn(Rect r,string text,Color? c=null)
        {
            uiRects.Add(r); Color old=GUI.color; GUI.color=c??Color.white;
            bool clicked=GUI.Button(r,text,button); GUI.color=old;
            return clicked && (state==RunState.Playing || state==RunState.Paused || state==RunState.Won || state==RunState.Lost);
        }
        void Bar(Rect r,float value,Color c) { Panel(r,new Color(.02f,.05f,.08f)); r.width*=Mathf.Clamp01(value); Panel(r,c); }
    }
}
