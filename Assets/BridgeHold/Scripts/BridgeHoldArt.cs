using System.Collections.Generic;
using UnityEngine;

namespace BridgeHold
{
    public sealed partial class BridgeHoldGame
    {
        Mesh softCube;
        Texture2D roundedPanel;
        readonly Texture2D[] elementIcons=new Texture2D[3];
        Mesh SoftCubeMesh()
        {
            if(softCube) return softCube;
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var triangles=new List<int>();
            float[] steps={-.5f,-.4f,.4f,.5f};
            Vector3[] faces={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            foreach(var n in faces)
            {
                Vector3 u=Vector3.Cross(n,Mathf.Abs(n.y)>.5f?Vector3.forward:Vector3.up),v=Vector3.Cross(n,u); int first=vertices.Count;
                foreach(float y in steps) foreach(float x in steps)
                {
                    Vector3 p=n*.5f+u*x+v*y, core=new Vector3(Mathf.Clamp(p.x,-.4f,.4f),Mathf.Clamp(p.y,-.4f,.4f),Mathf.Clamp(p.z,-.4f,.4f));
                    Vector3 normal=(p-core).normalized; vertices.Add(core+normal*.1f); normals.Add(normal);
                }
                for(int y=0;y<3;y++) for(int x=0;x<3;x++)
                { int a=first+y*4+x; triangles.AddRange(new[]{a,a+1,a+5,a,a+5,a+4}); }
            }
            softCube=new Mesh {name="Shared bevelled block"}; softCube.SetVertices(vertices); softCube.SetNormals(normals); softCube.SetTriangles(triangles,0); softCube.RecalculateBounds(); return softCube;
        }
        void AddScenicDetails(Material stone,Material wood,Material blue,Material brass)
        {
            var foam=Mat("water foam",new Color(.53f,.9f,.98f));
            for(int side=-1;side<=1;side+=2)
            {
                for(int z=-10;z<49;z+=7)
                {
                    for(int row=0;row<3;row++) Shape("Pillar course",PrimitiveType.Cube,new Vector3(side*6,.32f+row*.64f,z),new Vector3(1.12f,.58f,1.12f),stone);
                    var pole=Shape("Banner pole",PrimitiveType.Cylinder,new Vector3(side*5.45f,2.1f,z-.65f),new Vector3(.12f,.7f,.12f),brass); pole.rotation=Quaternion.Euler(0,0,90);
                    Shape("Banner gold edge",PrimitiveType.Cube,new Vector3(side*5.45f,1.05f,z-.68f),new Vector3(1.18f,1.9f,.1f),brass);
                    Shape("Blue pennant",PrimitiveType.Cube,new Vector3(side*5.45f,1.09f,z-.76f),new Vector3(.99f,1.76f,.06f),blue);
                    for(int k=-1;k<=1;k++) Shape("Crown stitch",PrimitiveType.Cube,new Vector3(side*5.45f+k*.23f,1.17f+Mathf.Abs(k)*.06f,z-.805f),new Vector3(.18f,.39f,.025f),brass);
                    Shape("Crown band",PrimitiveType.Cube,new Vector3(side*5.45f,.99f,z-.805f),new Vector3(.7f,.14f,.025f),brass);
                    Shape("Torch basket",PrimitiveType.Cylinder,new Vector3(side*6,2.3f,z),new Vector3(1.05f,.2f,1.05f),brass);
                    Shape("Torch core",PrimitiveType.Sphere,new Vector3(side*6,2.8f,z),new Vector3(.25f,.75f,.25f),Mat("flame core",new Color(1,.94f,.31f)));
                }
                for(int z=-8;z<49;z+=3)
                {
                    Shape("Wood rail",PrimitiveType.Cube,new Vector3(side*5.98f,1.7f,z),new Vector3(.3f,.32f,2.85f),wood);
                    Shape("Water shimmer",PrimitiveType.Cube,new Vector3(side*(8+Mathf.Abs(Mathf.Sin(z))*3),-1.72f,z),new Vector3(1.6f,.025f,.12f),foam);
                    for(int j=0;j<2;j++)
                    { var leaf=Shape("Bridge ivy",PrimitiveType.Sphere,new Vector3(side*(5.45f-j*.13f),.16f,z+j*.34f),new Vector3(.3f,.09f,.62f),Mat("ivy",new Color(.34f,.57f,.1f))); leaf.rotation=Quaternion.Euler(0,z*17+j*30,12); }
                }
            }
            Shape("Distant waterfall",PrimitiveType.Cube,new Vector3(0,-.3f,49),new Vector3(17,5,.2f),Mat("waterfall",new Color(.46f,.82f,.99f)));
            for(int i=-7;i<=7;i+=2) Shape("Waterfall ribbon",PrimitiveType.Cube,new Vector3(i,-.3f,48.85f),new Vector3(.18f,5,.05f),foam);
        }
        void AddPlayerDetails(Material blue,Material brass,Material leather)
        {
            blue.SetFloat("_Glossiness",.62f); var dark=Mat("boot leather",new Color(.075f,.105f,.15f));
            for(int side=-1;side<=1;side+=2)
            {
                Shape("Rounded boot",PrimitiveType.Capsule,new Vector3(side*.3f,.25f,.02f),new Vector3(.46f,.28f,.7f),dark,player);
                Shape("White sleeve",PrimitiveType.Sphere,new Vector3(side*.61f,1.17f,.15f),new Vector3(.48f,.45f,.5f),Mat("sleeves",new Color(.92f,.93f,.85f)),player);
                Shape("Glove",PrimitiveType.Sphere,new Vector3(side*.59f,1.12f,.48f),Vector3.one*.32f,leather,player);
                Shape("Backpack strap",PrimitiveType.Cube,new Vector3(side*.28f,1.12f,-.8f),new Vector3(.13f,.88f,.07f),brass,player);
                Shape("Helmet rivet",PrimitiveType.Sphere,new Vector3(side*.49f,1.94f,-.4f),Vector3.one*.09f,brass,player);
            }
            Shape("Helmet rim",PrimitiveType.Cylinder,new Vector3(0,1.89f,.01f),new Vector3(1.34f,.075f,1.26f),blue,player);
            Shape("Backpack flap",PrimitiveType.Cube,new Vector3(0,1.45f,-.7f),new Vector3(.94f,.3f,.24f),leather,player);
            Shape("Backpack buckle",PrimitiveType.Cube,new Vector3(0,1.16f,-.83f),new Vector3(.24f,.2f,.08f),brass,player);
        }
        void AddCannonDetails(Transform mount,Transform barrel,Material brass)
        {
            var dark=Mat("cannon dark",new Color(.055f,.1f,.18f)); var assembly=Group("Cannon muzzle rings",barrel);
            assembly.localPosition=new Vector3(0,.6f,.25f); assembly.localRotation=Quaternion.Euler(48,0,0);
            Shape("Gold muzzle ring",PrimitiveType.Cylinder,new Vector3(0,.75f,0),new Vector3(1.02f,.14f,1.02f),brass,assembly);
            Shape("Bore opening",PrimitiveType.Cylinder,new Vector3(0,.9f,0),new Vector3(.73f,.018f,.73f),dark,assembly);
            for(int side=-1;side<=1;side+=2)
            {
                var wheel=Shape("Cannon wheel",PrimitiveType.Cylinder,new Vector3(side*.69f,.03f,0),new Vector3(.8f,.13f,.8f),dark,mount); wheel.localRotation=Quaternion.Euler(0,0,90);
                var hub=Shape("Brass hub",PrimitiveType.Cylinder,new Vector3(side*.84f,.03f,0),new Vector3(.36f,.06f,.36f),brass,mount); hub.localRotation=Quaternion.Euler(0,0,90);
            }
        }
        void AddWallDetails()
        {
            var iron=Mat("wall iron",new Color(.2f,.25f,.3f)); var trim=Mat("wall brass fittings",new Color(.89f,.59f,.2f));
            foreach(float y in new[]{.2f,1.91f})
            {
                var beam=Shape("Wall reinforcement",PrimitiveType.Cube,new Vector3(0,y,-.73f),new Vector3(11.4f,.18f,.17f),iron,wall); wallParts.Add(beam.gameObject);
                for(int i=-5;i<=5;i++) { var bolt=Shape("Wall brass bolt",PrimitiveType.Sphere,new Vector3(i,y,-.85f),Vector3.one*.115f,trim,wall); wallParts.Add(bolt.gameObject); }
            }
            for(int i=-5;i<=5;i++) { var spike=Shape("Wall crest",PrimitiveType.Cube,new Vector3(i,2.23f,0),new Vector3(.43f,.45f,.43f),trim,wall); spike.localRotation=Quaternion.Euler(0,0,45); wallParts.Add(spike.gameObject); }
        }
        void AddZombieDetails(Transform root,int index,Material skin,Material shirt,Material dark)
        {
            for(int side=-1;side<=1;side+=2)
            {
                Shape("Zombie boot",PrimitiveType.Capsule,new Vector3(side*.2f,.15f,-.04f),new Vector3(.28f,.2f,.4f),dark,root);
                var arm=Shape("Zombie arm",PrimitiveType.Capsule,new Vector3(side*.41f,.64f,-.09f),new Vector3(.2f,.28f,.22f),shirt,root); arm.localRotation=Quaternion.Euler(-22,0,side*24);
                Shape("Zombie hand",PrimitiveType.Sphere,new Vector3(side*.47f,.43f,-.19f),Vector3.one*.22f,skin,root);
                Shape("Zombie tooth",PrimitiveType.Cube,new Vector3(side*.09f,1.13f,-.445f),new Vector3(.085f,.12f,.045f),Mat("teeth",new Color(1,.93f,.7f)),root);
            }
            if(index%4==0)
            {
                var helmet=Mat("enemy helmet "+(index%3),index%3==0?new Color(.78f,.13f,.92f):new Color(1,.57f,.04f));
                Shape("Zombie helmet",PrimitiveType.Sphere,new Vector3(0,1.69f,0),new Vector3(.93f,.34f,.83f),helmet,root);
                Shape("Zombie helmet brim",PrimitiveType.Cylinder,new Vector3(0,1.59f,-.06f),new Vector3(1,.045f,.92f),helmet,root);
            }
        }
        Texture2D RoundedPanelTexture()
        {
            if(roundedPanel) return roundedPanel; roundedPanel=new Texture2D(32,32,TextureFormat.RGBA32,false);
            for(int y=0;y<32;y++) for(int x=0;x<32;x++)
            { float d=new Vector2(Mathf.Max(0,Mathf.Max(7-x,x-24)),Mathf.Max(0,Mathf.Max(7-y,y-24))).magnitude; roundedPanel.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(8-d))); }
            roundedPanel.Apply(); return roundedPanel;
        }
        void CardPanel(Rect r,Color c)
        {
            Color old=GUI.color; GUI.color=new Color(0,0,0,.4f); GUI.DrawTexture(new Rect(r.x,r.y+3,r.width,r.height),RoundedPanelTexture()); GUI.color=c; GUI.DrawTexture(r,RoundedPanelTexture()); GUI.color=old;
        }
        void DrawElementBadge(Rect r,int element,bool selected)
        {
            if(!elementIcons[element])
            {
                var texture=new Texture2D(64,64,TextureFormat.RGBA32,false);
                for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                {
                    Vector2 p=new Vector2((x-31.5f)/28,(y-31.5f)/28); bool mark=false;
                    if(element==0)
                    {
                        for(int axis=0;axis<3;axis++)
                        {
                            float angle=axis*Mathf.PI/3,a=p.x*Mathf.Cos(angle)+p.y*Mathf.Sin(angle),b=-p.x*Mathf.Sin(angle)+p.y*Mathf.Cos(angle);
                            if(Mathf.Abs(b)<.045f && Mathf.Abs(a)<.8f || Mathf.Abs(Mathf.Abs(b)-(.65f-Mathf.Abs(a)))<.05f && Mathf.Abs(a)>.38f && Mathf.Abs(a)<.66f) mark=true;
                        }
                    }
                    else if(element==1) mark=p.y>-.75f && p.y<.9f && Mathf.Abs(p.x)<(.65f*(1-p.y)*Mathf.Sqrt(Mathf.Max(0,p.y+.75f))) && !(p.y<.05f && p.y>-.65f && Mathf.Abs(p.x)<.18f*(1-p.y));
                    else mark=p.y>-.85f && p.y<.85f && Mathf.Abs(p.x-(p.y>0?.1f:-.1f)-p.y*.48f)<.17f || Mathf.Abs(p.y)<.09f && Mathf.Abs(p.x)<.44f;
                    texture.SetPixel(x,y,mark?Color.white:Color.clear);
                }
                texture.Apply(); elementIcons[element]=texture;
            }
            CardPanel(r,selected?elementColors[element]*.8f:navy); Color old=GUI.color; GUI.color=selected?Color.white:elementColors[element]; GUI.DrawTexture(r,elementIcons[element]); GUI.color=old;
        }
    }
}
