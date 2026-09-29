using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BridgeHold.Editor
{
    [InitializeOnLoad]
    public static class BridgeHoldSmoke
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static double readyAt;
        static BridgeHoldSmoke() { if(SessionState.GetBool("BridgeHoldSmoke",false)) EditorApplication.update+=Tick; }
        public static void Run()
        {
            BridgeHoldSetup.CreateScene();
            SessionState.SetBool("BridgeHoldSmoke",true);
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            EditorApplication.isPlaying=true;
        }
        static object Get(object o,string name) => o.GetType().GetField(name,Private|BindingFlags.Public).GetValue(o);
        static void Set(object o,string name,object value) => o.GetType().GetField(name,Private|BindingFlags.Public).SetValue(o,value);
        static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,Private).Invoke(o,args);
        static void AlignLane(BridgeHoldGame game,Vector3 target)
        {
            var muzzle=(Transform)Get(game,"muzzle");
            Call(game,"MovePlayer",target.x-muzzle.position.x);
        }
        static void Fire(BridgeHoldGame game)
        {
            Call(game,"Shoot"); Call(game,"UpdatePeas",3f);
        }
        static int ActivePeas(BridgeHoldGame game)
        {
            int count=0; foreach(var pea in (IList)Get(game,"peas")) if(((Transform)Get(pea,"root")).gameObject.activeSelf) count++;
            return count;
        }
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); Debug.Log("SMOKE PASS: "+message); }
        static void Tick()
        {
            if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if(readyAt==0) { readyAt=EditorApplication.timeSinceStartup+3; return; }
            if(EditorApplication.timeSinceStartup<readyAt) return;
            EditorApplication.update-=Tick; SessionState.SetBool("BridgeHoldSmoke",false);
            try
            {
                var game=UnityEngine.Object.FindFirstObjectByType<BridgeHoldGame>(); Check(game!=null,"scene starts a game");
                Call(game,"Restart");
                Check(((IList)Get(game,"enemies")).Count==300,"300 reusable enemy slots");
                Check((float)Get(game,"wallHp")==450 && (float)Get(game,"playerHp")==100,"restart restores health");
                var player=(Transform)Get(game,"player");
                float originalZ=player.position.z;
                Call(game,"MovePlayer",2f);
                Check(player.position.x==2 && player.position.z==originalZ,"player moves horizontally only");
                Call(game,"MovePlayer",100f); Check(player.position.x==4.8f,"right bridge boundary clamps movement");
                Call(game,"MovePlayer",-100f); Check(player.position.x==-4.8f,"left bridge boundary clamps movement");
                Call(game,"TogglePause"); Call(game,"MovePlayer",3f);
                Check(player.position.x==-4.8f,"paused player cannot move");
                Call(game,"TogglePause"); Call(game,"Restart");
                Check(player.position.x==0,"restart centers player");
                Set(game,"fireTimer",.4f);
                Call(game,"SelectWeapon",1);
                Check(Get(game,"activeWeapon").ToString()=="TriplePea" && Get(game,"state").ToString()=="Playing","weapon switch keeps combat running");
                Check((float)Get(game,"fireTimer")==.4f,"switch preserves shot cooldown");
                Call(game,"SelectWeapon",1);
                Check(Get(game,"activeWeapon").ToString()=="TriplePea" && (float)Get(game,"fireTimer")==.4f,"clicking equipped card keeps same weapon and cooldown");
                Call(game,"SelectWeapon",0); Check(Get(game,"activeWeapon").ToString()=="Pea","first card directly equips single pea");
                Call(game,"SelectWeapon",1); Check(Get(game,"activeWeapon").ToString()=="TriplePea","second card directly equips triple pea");
                Rect firstCard=(Rect)Call(game,"WeaponCardRect",0), secondCard=(Rect)Call(game,"WeaponCardRect",1);
                Check(firstCard.xMax<secondCard.xMin && firstCard.y==secondCard.y,"two weapon cards sit side by side without overlap");
                var models=(Transform[])Get(game,"weaponModels");
                Check(!models[0].gameObject.activeSelf && models[1].gameObject.activeSelf,"equipped weapon matches visible model");
                Call(game,"MovePlayer",1f); Check(player.position.x==1f,"movement continues with triple pea equipped");
                Set(game,"crateAlive",true); Set(game,"crateHp",200f);
                var testCrate=(Transform)Get(game,"crate"); testCrate.position=new Vector3(1.55f,1,6);
                Call(game,"Shoot");
                Check(ActivePeas(game)==3,"triple pea creates three visible projectiles");
                Check((float)Get(game,"crateHp")==200f,"peas do not deal damage before impact");
                Call(game,"UpdatePeas",3f);
                Check(Mathf.Approximately((float)Get(game,"crateHp"),128f),"three peas each deal base damage on impact");
                Check(ActivePeas(game)==0,"impact returns peas to pool");
                Call(game,"TogglePause"); Call(game,"SelectWeapon",0);
                Check(Get(game,"activeWeapon").ToString()=="TriplePea","paused game rejects weapon switching");
                Call(game,"TogglePause"); Call(game,"SelectWeapon",0);
                Check((float)Get(game,"damage")==24f && (int)Get(game,"weaponLevel")==1,"switching preserves shared upgrades");
                Call(game,"Restart");
                Check(Get(game,"activeWeapon").ToString()=="Pea" && models[0].gameObject.activeSelf && (float)Get(game,"fireTimer")==0,"restart restores pea shooter and cooldown");
                Call(game,"Shoot"); Check(ActivePeas(game)==1,"single pea creates one projectile");
                Vector3 peaPosition=((Transform)Get(((IList)Get(game,"peas"))[0],"root")).position;
                Call(game,"SelectWeapon",1);
                Check(ActivePeas(game)==1,"switching preserves peas in flight");
                Call(game,"UpdatePeas",.1f);
                Check(((Transform)Get(((IList)Get(game,"peas"))[0],"root")).position.z>peaPosition.z,"pea visibly travels forward");
                Call(game,"Restart"); Check(ActivePeas(game)==0,"restart clears projectiles");
                Call(game,"UpdateWaves",3f);
                var list=(IList)Get(game,"enemies"); object enemy=null;
                foreach(var e in list) if((bool)Get(e,"alive")) { enemy=e; break; }
                Check(enemy!=null,"wave spawns enemies");
                var root=(Transform)Get(enemy,"root"); root.position=new Vector3(0,0,5.5f);
                Set(enemy,"attackAt",0f); Call(game,"UpdateEnemies",.01f);
                Check((float)Get(game,"wallHp")<450 && (float)Get(game,"playerHp")==100,"wall absorbs attacks before player");
                Set(game,"wallHp",1f); Set(enemy,"attackAt",0f); Call(game,"UpdateEnemies",.01f);
                Check((float)Get(game,"wallHp")==0 && !((Transform)Get(game,"wall")).gameObject.activeSelf,"wall destruction removes full wall");
                root.position=new Vector3(0,0,-5); Set(enemy,"attackAt",0f); Call(game,"UpdateEnemies",.01f);
                Check((float)Get(game,"playerHp")<100,"breached enemies can attack player");
                Call(game,"Hurt",enemy,100000f,Color.white);
                Check(!(bool)Get(enemy,"alive") && (int)Get(game,"kills")==1,"death returns enemy to pool and counts kill");
                Call(game,"Restart");
                Call(game,"UpdateWaves",3f);
                var left=list[0]; var right=list[1];
                foreach(var candidate in list) Set(candidate,"alive",false);
                Set(left,"alive",true); Set(right,"alive",true);
                ((Transform)Get(left,"root")).position=new Vector3(-3,0,8);
                ((Transform)Get(right,"root")).position=new Vector3(3,0,10);
                Call(game,"UpdateTurrets",.01f);
                var targets=(Array)Get(game,"turretTargets");
                Check(ReferenceEquals(targets.GetValue(0),left) && ReferenceEquals(targets.GetValue(1),right),"turrets independently attack their own sides");
                var shellList=(IList)Get(game,"shells");
                Check((Vector3)Get(shellList[0],"end")!=(Vector3)Get(shellList[1],"end"),"simultaneous shells use different impact positions");
                ((Transform)Get(left,"root")).position=new Vector3(2,0,8);
                Check(ReferenceEquals(Call(game,"SelectTurretTarget",0),left),"turret covers opposite side without duplicating target");
                Set(left,"alive",false);
                Check(ReferenceEquals(Call(game,"SelectTurretTarget",0),right),"single remaining enemy can be attacked by both turrets");
                Set(right,"alive",false);
                Check(Call(game,"SelectTurretTarget",0)==null,"no living enemies means no target");
                Call(game,"Restart");
                Check(targets.GetValue(0)==null && targets.GetValue(1)==null,"restart clears both turret targets");
                Set(game,"crateAlive",true); Set(game,"crateHp",1f);
                var crate=(Transform)Get(game,"crate"); crate.position=new Vector3(0,1,12); crate.gameObject.SetActive(true);
                AlignLane(game,new Vector3(3,1,12)); Fire(game);
                Check((bool)Get(game,"crateAlive") && (float)Get(game,"crateHp")==1f,"forward shot cannot hit crate in another lane");
                Check(Vector3.Dot(((Transform)Get(game,"gun")).forward,Vector3.forward)>.999f,"weapon points along bridge after strafing");
                AlignLane(game,crate.position); Fire(game);
                Check(Get(game,"state").ToString()=="Playing" && (bool)Get(game,"rewardActive") && !(bool)Get(game,"crateAlive"),"crate opens world rewards without pausing combat");
                var blocks=(Transform[])Get(game,"rewardBlocks");
                int beforePoints=(int)Get(game,"points"); float beforeDamage=(float)Get(game,"damage");
                AlignLane(game,blocks[0].position); Fire(game); Fire(game);
                Check((bool)Get(game,"rewardActive") && (int)Get(game,"rewardHits")==2 && (float)Get(game,"damage")==beforeDamage,"two shots do not grant a reward");
                AlignLane(game,blocks[1].position); Fire(game);
                Check((int)Get(game,"rewardHits")==1 && (int)Get(game,"rewardSide")==1,"switching target resets shot streak");
                AlignLane(game,new Vector3(0,1,30)); Fire(game);
                Check((int)Get(game,"rewardHits")==0,"missing both reward blocks breaks streak");
                AlignLane(game,blocks[1].position); Fire(game); Fire(game); Fire(game);
                Check(!(bool)Get(game,"rewardActive") && (int)Get(game,"points")==beforePoints+40 && (float)Get(game,"damage")==beforeDamage,"three shots choose defense only");
                Check(!blocks[0].gameObject.activeSelf && !blocks[1].gameObject.activeSelf,"both blocks disappear after selection");
                Fire(game); Check((int)Get(game,"points")==beforePoints+40,"reward is granted once");
                Call(game,"OpenRewards"); AlignLane(game,blocks[0].position);
                Fire(game); Fire(game); Fire(game);
                Check((float)Get(game,"damage")==beforeDamage*1.25f && (int)Get(game,"weaponLevel")==2,"three shots choose weapon upgrade");
                Call(game,"OpenRewards"); Call(game,"SelectWeapon",1); AlignLane(game,blocks[0].position);
                Fire(game); Check((int)Get(game,"rewardHits")==1 && (bool)Get(game,"rewardActive"),"triple pea counts only one reward hit per volley");
                Fire(game); Fire(game); Check(!(bool)Get(game,"rewardActive"),"triple pea still needs three volleys to select reward");
                Call(game,"OpenRewards"); Call(game,"Restart");
                Check(!(bool)Get(game,"rewardActive") && !blocks[0].gameObject.activeSelf,"restart clears pending rewards");
                Call(game,"Restart"); Call(game,"TogglePause"); Check(Get(game,"state").ToString()=="Paused","pause works");
                Call(game,"TogglePause"); Check(Get(game,"state").ToString()=="Playing","resume works");
                Set(game,"wave",10); Call(game,"UpdateWaves",1f); Check(Get(game,"state").ToString()=="Won","last cleared wave wins");
                Call(game,"Restart");
                var plates=(Renderer[])Get(game,"elementPlates");
                Check(plates.Length==3 && plates[0].transform.parent==(Transform)Get(game,"wall") && plates[0].transform.localPosition.z<0,"element blocks are mounted on player-facing wall");
                var buffTarget=list[0]; var neighbor=list[1];
                Set(buffTarget,"alive",true); Set(buffTarget,"hp",1000f); Set(buffTarget,"speed",1f);
                var buffRoot=(Transform)Get(buffTarget,"root"); buffRoot.position=new Vector3(.55f,0,8);
                Call(game,"SelectElement",0); Call(game,"Shoot");
                Call(game,"SelectElement",1); Call(game,"UpdatePeas",3f);
                Check((float)Get(buffTarget,"hp")==976f && (float)Get(buffTarget,"slowUntil")>0,"in-flight ice pea retains slow after element switch");
                float beforeZ=buffRoot.position.z; Call(game,"UpdateEnemies",.1f);
                Check(buffRoot.position.z>beforeZ-.1f && buffRoot.position.z<beforeZ,"ice pea actually slows enemy movement");
                float beforeFire=(float)Get(buffTarget,"hp"); Fire(game);
                Check(Mathf.Approximately(beforeFire-(float)Get(buffTarget,"hp"),36f),"fire pea increases direct damage by fifty percent");
                buffRoot.position=new Vector3(.55f,0,0); Call(game,"SelectWeapon",1);
                beforeFire=(float)Get(buffTarget,"hp"); Fire(game);
                Check(Mathf.Approximately(beforeFire-(float)Get(buffTarget,"hp"),108f),"fire enhancement applies to all three peas");
                Call(game,"SelectWeapon",0); Call(game,"SelectElement",2);
                Set(neighbor,"alive",true); Set(neighbor,"hp",1000f);
                ((Transform)Get(neighbor,"root")).position=new Vector3(2.05f,0,0);
                Fire(game);
                Check((float)Get(neighbor,"hp")<1000f,"electric pea chains damage to nearby enemy");
                Call(game,"Restart");
                var wallFx=(Transform[])Get(game,"wallElementFxGroups");
                Check(wallFx[0].gameObject.activeSelf && !wallFx[1].gameObject.activeSelf && !wallFx[2].gameObject.activeSelf,"restart shows only ice wall effect");
                Call(game,"SelectElement",1);
                Check(!wallFx[0].gameObject.activeSelf && wallFx[1].gameObject.activeSelf && !wallFx[2].gameObject.activeSelf,"fire selection replaces ice wall effect");
                Check((float)Get(game,"wallElementPulse")>.8f,"element selection triggers wall flash");
                Call(game,"UpdateWallElementEffects",1f);
                Check((float)Get(game,"wallElementPulse")==0f && wallFx[1].gameObject.activeSelf,"selection flash settles while ambient flames remain");
                Call(game,"SelectElement",2);
                Check(!wallFx[1].gameObject.activeSelf && wallFx[2].gameObject.activeSelf,"electric selection replaces flames with arc");
                Set(game,"wallHp",0f); Call(game,"UpdateWallElementEffects",.1f);
                Check(!((Transform)Get(game,"wallElementFxRoot")).gameObject.activeSelf,"destroyed wall hides elemental effects");
                Call(game,"Restart");
                Check(((Transform)Get(game,"wallElementFxRoot")).gameObject.activeSelf && wallFx[0].gameObject.activeSelf,"restart restores wall effects without rebuilding the pool");
                Debug.Log("BRIDGEHOLD_SMOKE_OK"); EditorApplication.Exit(0);
            }
            catch(Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
