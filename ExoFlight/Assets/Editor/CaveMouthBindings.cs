using UnityEngine;
using UnityEditor;
namespace ExoFlight.EditorTools {
public static class CaveMouthBindings {
 public static void Apply(){
  const string dir="Assets/MarsMission/";
  var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(dir+"Continuous lava tube.asset");if(!mesh)return;
  var cave=GameObject.Find("Lava tube walls, roof and floor"); var d=Terrain.activeTerrain.terrainData;
  var old=GameObject.Find("Eroded rock portal");if(old)old.SetActive(false);
  cave.GetComponent<MeshFilter>().sharedMesh=mesh;cave.GetComponent<MeshCollider>().sharedMesh=mesh;cave.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(dir+"Cave geology.mat");
  int n=d.holesResolution;var h=d.GetHoles(0,0,n,n);for(int z=0;z<n;z++)for(int x=0;x<n;x++){
   if ((x>=257 && x<295 && z>=305 && z<339) || (x>=313 && x<351 && z>=446 && z<485))h[z,x]=false;
  }  d.SetHoles(0,0,h);Terrain.activeTerrain.heightmapPixelError=1;Terrain.activeTerrain.Flush();EditorUtility.SetDirty(d);AssetDatabase.SaveAssetIfDirty(d);
 }
}}


