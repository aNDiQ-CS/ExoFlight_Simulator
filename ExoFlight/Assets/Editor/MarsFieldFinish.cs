using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using static UnityEngine.Mathf;

namespace ExoFlight.EditorTools {
public static class MarsFieldFinish {
    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/MarsLandscape/"+name+".mat");
    static float Ease(float a,float b,float x)=>SmoothStep(0,1,InverseLerp(a,b,x));
    [MenuItem("ExoFlight/Марс: обустроить полевой маршрут")]
    public static void Apply(){
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(Application.isPlaying || scene.path!="Assets/Scenes/MarsMission.unity") throw new System.InvalidOperationException("Open MarsMission in edit mode");
        var terrain=Terrain.activeTerrain;var d=terrain.terrainData;
        if(!AssetDatabase.GetAssetPath(d).EndsWith("/Through cave terrain.asset"))throw new System.InvalidOperationException("Wrong terrain");
        var zone=GameObject.Find("Pickup - rover").transform;
        var origin=zone.position;var mouth=MarsCaveExitSetup.Mouth;
        var forward=new Vector2(9,7).normalized;var right=new Vector2(forward.y,-forward.x);
        MarsCaveExitSetup.RestoreMissionLandform(d);var heights=d.GetHeights(0,0,d.heightmapResolution,d.heightmapResolution);
        var baseline=AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/MarsLandscape/Ares Basin Terrain.asset");
        int n=d.heightmapResolution;var baselineHeights=baseline.GetHeights(0,0,n,n);
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){
            float X=x*d.size.x/(n-1),Z=z*d.size.z/(n-1);
            float h=heights[z,x]*d.size.y;
            float apron=Max(Abs(X-origin.x)/7.7f,Abs(Z-origin.z)/6f);
            if(apron<2.5f)h=Lerp(origin.y-.085f,baselineHeights[z,x]*d.size.y,Ease(1,2.5f,apron));
            var delta=new Vector2(X-mouth.x,Z-mouth.z);float s=Vector2.Dot(delta,forward),r=Vector2.Dot(delta,right);
            if(s>-38 && s<47 && Abs(r)<33){
                float width=7.5f+2.2f*Sin(s*.095f)+1.1f*Sin(s*.22f);
                float core=1-Ease(width,width+14,Abs(r-1.5f*Sin(s*.12f)));
                float start=Ease(-31,-20,s),end=1-Ease(18,47,s);
                float floor=mouth.y-10+Min(0,s)*.25f+1.3f*PerlinNoise(X*.10f,Z*.10f);
                h=Lerp(h,Min(h,floor),core*start*end);
            }
            heights[z,x]=h/d.size.y;
        }
        d.SetHeights(0,0,heights);
        int hn=d.holesResolution;var holes=d.GetHoles(0,0,hn,hn);
        for(int z=0;z<hn;z++)for(int x=0;x<hn;x++){
            float u=(x+.5f)/hn,v=(z+.5f)/hn;
            var delta=new Vector2(u*d.size.x-mouth.x,v*d.size.z-mouth.z);
            if(delta.sqrMagnitude>35*35)continue;
            float s=Vector2.Dot(delta,forward),r=Vector2.Dot(delta,right);
            float h=d.GetInterpolatedHeight(u,v),cy=mouth.y+Min(0,s)*.25f;
            float roof=cy+7-Max(0,Abs(r)-3)*.75f;
            holes[z,x]=!(s< -19 && s> -31 && Abs(r)<10 && h<roof && h>cy-9.2f);
        }
                // Seat the vertices around the Terrain hole inside the existing tunnel lining.
        // The hole grid is coarser than the opening: without overlap the sky shows through.
        for(int z=1;z<n-1;z++)for(int x=1;x<n-1;x++){
            var delta=new Vector2(x*d.size.x/(n-1)-mouth.x,z*d.size.z/(n-1)-mouth.z);
            if(delta.sqrMagnitude>35*35)continue;
            int hx=x*hn/(n-1),hz=z*hn/(n-1);
            if(holes[hz,hx] && holes[hz-1,hx] && holes[hz,hx-1] && holes[hz-1,hx-1])continue;
            float along=Vector2.Dot(delta,forward),lateral=Abs(Vector2.Dot(delta,right));
            float seal=mouth.y+along*.25f+5.8f-Max(0,lateral-2)*.82f;
            heights[z,x]=Min(heights[z,x],seal/d.size.y);
        }
        d.SetHeights(0,0,heights);d.SetHoles(0,0,holes);terrain.Flush();EditorUtility.SetDirty(d);
        var arch=GameObject.Find("Weathered exit arch and ground seam");if(arch)arch.SetActive(false);
        foreach(var c in zone.Cast<Transform>().Where(v=>v.name=="Pad edge"||v.name=="Landing deck").ToArray())c.gameObject.SetActive(false);
        if(!zone.Find("Field loading deck")){
            var go=new GameObject("Field loading deck");go.transform.SetParent(zone,false);
            go.transform.localScale=new Vector3(.56f,.55f,.55f);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/MarsMission/Recovery station deck.asset");
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/MarsMission/Dust worn recovery deck.mat");
            go.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        var masts=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(v=>v.name=="Mast"&&v.parent.Find("Reflector")).ToArray();
        foreach(var mast in masts){
            var root=mast.parent;var pos=root.position;var tint=root.Find("Reflector").GetComponent<Renderer>().sharedMaterial;
            foreach(var part in root.Cast<Transform>().ToArray())part.gameObject.SetActive(false);
            if(root.name.StartsWith("Approach")||root.name=="Cave mouth lamp"){
                if((pos-origin).sqrMagnitude<100){pos.x=origin.x+7.4f;pos.z=origin.z-5.2f;}
                pos.y=terrain.SampleHeight(pos);root.position=pos;
            }
            var body=Mat("Graphite hardware"); var metal=Mat("FPV titanium");
            void Part(string name,PrimitiveType type,Vector3 p,Vector3 scale,Material mat,Quaternion rot){
                var g=GameObject.CreatePrimitive(type);g.name=name;Object.DestroyImmediate(g.GetComponent<Collider>());g.transform.SetParent(root,false);
                g.transform.localPosition=p;g.transform.localRotation=rot;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=mat;
            }
            for(int i=0;i<3;i++){
                var foot=Quaternion.Euler(0,i*120,0)*new Vector3(.58f,.025f,0);var shoulder=Vector3.up*.64f;
                Part("Anchor shoe",PrimitiveType.Cylinder,foot,new Vector3(.29f,.055f,.29f),body,Quaternion.identity);
                Part("Tripod strut",PrimitiveType.Cylinder,(foot+shoulder)*.5f,new Vector3(.092f,(shoulder-foot).magnitude*.5f,.092f),metal,Quaternion.FromToRotation(Vector3.up,shoulder-foot));
            }
            Part("Receiver",PrimitiveType.Cylinder,Vector3.up*.78f,new Vector3(.42f,.18f,.42f),body,Quaternion.identity);
            Part("Fresnel lens",PrimitiveType.Cylinder,Vector3.up*1.05f,new Vector3(.32f,.12f,.32f),tint,Quaternion.identity);
            Part("Sun visor",PrimitiveType.Cylinder,Vector3.up*1.19f,new Vector3(.49f,.035f,.49f),metal,Quaternion.identity);
        }
        CaveMouthBindings.Apply();Physics.SyncTransforms();AssetDatabase.SaveAssetIfDirty(d);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
}}

