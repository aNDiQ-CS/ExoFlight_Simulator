using System;
using System.Collections.Generic;
using System.Linq;
using ExoFlight.Missions.Mars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ExoFlight.EditorTools
{
    /// <summary>Builds the mission-only cave continuation and a grounded recovery station.</summary>
    public static class MarsCaveExitSetup
    {
        const string Folder = "Assets/MarsMission/";
        const string Landscape = "Assets/MarsLandscape/";
        public static readonly Vector3 Mouth = new Vector3(623f, 34f, 922f);
        public static readonly Vector3 Pad = new Vector3(678f, 0f, 934f);
        static readonly Vector3[] Tunnel = {
            new Vector3(579.51f, 10.04f, 870f), new Vector3(586f, 15f, 885f),
            new Vector3(600f, 23f, 900f), new Vector3(614f, 31f, 915f), Mouth
        };
        static readonly Vector3 ExitForward = new Vector3(9f, 0f, 7f).normalized;
        // Deliberately asymmetric, with a broad flat floor and a fractured, leaning roof.
        static readonly Vector2[] Opening = {
            new Vector2(-14f,-9f), new Vector2(-13f,-2.5f), new Vector2(-10.8f,4f),
            new Vector2(-7.2f,8.4f), new Vector2(-2.5f,10.3f), new Vector2(3.2f,8.3f),
            new Vector2(8.6f,6.1f), new Vector2(11.8f,1.7f), new Vector2(13.5f,-9f)
        };

        [MenuItem("ExoFlight/Марс: обновить скальный выход")]
        public static void RefreshCaveOnly()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var mission = Object.FindFirstObjectByType<MarsMissionDirector>();
            if (mission == null || mission.gameObject.scene.path != MarsMissionBuilder.ScenePath)
                throw new InvalidOperationException("Open MarsMission first.");
            var terrain = Terrain.activeTerrain;
            if (!AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(Folder))
                throw new InvalidOperationException("Install the through-cave mission first.");
            SculptTerrain(terrain.terrainData, AssetDatabase.LoadAssetAtPath<TerrainData>(Landscape + "Ares Basin Terrain.asset"),
                mission.cargoMission.deliveryZone.transform.position.y);
            var cave = GameObject.Find("Lava tube walls, roof and floor");
            Mesh mesh = SaveMesh("Through cave walls and rising exit", ContinueCave(
                AssetDatabase.LoadAssetAtPath<Mesh>(Landscape + "Lava tube walls, roof and floor.asset")));
            cave.GetComponent<MeshFilter>().sharedMesh = mesh;
            cave.GetComponent<MeshCollider>().sharedMesh = null;
            cave.GetComponent<MeshCollider>().sharedMesh = mesh;
            Transform scenery = mission.transform.Find("Cave exit and recovery station");
            var old = scenery.Find("Weathered exit arch and ground seam");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            MarsFieldFinish.Apply(); terrain.Flush(); Physics.SyncTransforms();
            EditorUtility.SetDirty(terrain.terrainData);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
            EditorSceneManager.SaveScene(mission.gameObject.scene);
        }

        [MenuItem("ExoFlight/Марс: сквозная пещера и площадка доставки")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var mission = Object.FindFirstObjectByType<MarsMissionDirector>();
            if (mission == null || mission.gameObject.scene.path != MarsMissionBuilder.ScenePath)
                throw new InvalidOperationException("Open MarsMission first.");
            var terrain = Terrain.activeTerrain;
            var original = AssetDatabase.LoadAssetAtPath<TerrainData>(Landscape + "Ares Basin Terrain.asset");
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(Folder + "Through cave terrain.asset");
            if (data == null)
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), Folder + "Through cave terrain.asset");
                data = AssetDatabase.LoadAssetAtPath<TerrainData>(Folder + "Through cave terrain.asset");
            }
            terrain.terrainData = data;
            terrain.GetComponent<TerrainCollider>().terrainData = data;
            float floor = original.GetInterpolatedHeight(Pad.x / original.size.x, Pad.z / original.size.z) + .18f;
            SculptTerrain(data, original, floor);

            var cave = GameObject.Find("Lava tube walls, roof and floor");
            var sourceMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Landscape + "Lava tube walls, roof and floor.asset");
            Mesh continuation = ContinueCave(sourceMesh);
            Mesh saved = SaveMesh("Through cave walls and rising exit", continuation);
            cave.GetComponent<MeshFilter>().sharedMesh = saved;
            cave.GetComponent<MeshCollider>().sharedMesh = null;
            cave.GetComponent<MeshCollider>().sharedMesh = saved;
            var stone = MaterialCopy("Through cave stone", "Fractured lava tube stone");
            stone.SetFloat("_Cull", 0f);
            cave.GetComponent<Renderer>().sharedMaterial = stone;

            Transform scenery = Child(mission.transform, "Cave exit and recovery station");
            ClearChildren(scenery);

            Vector3 landingFloor = new Vector3(Pad.x, floor, Pad.z);
            BuildLandingPad(mission, scenery, landingFloor);
            BuildRoute(mission, landingFloor); MarsFieldFinish.Apply();
            Physics.SyncTransforms();
            terrain.Flush();
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(stone);
            AssetDatabase.SaveAssets();
            var pilot = mission.drone.GetComponent<DroneMissionAutopilot>();
            if (pilot == null) MarsPresentationSetup.Install();
            else
            {
                pilot.loaded = mission.assist.routeAfterPickup;
                pilot.landing = mission.assist.routeLanding;
                EditorUtility.SetDirty(pilot);
                EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
                EditorSceneManager.SaveScene(mission.gameObject.scene);
            }
            Debug.Log("[Mars cave exit] Through route installed. Delivery floor " + landingFloor);
        }

        internal static void RestoreMissionLandform(TerrainData d)
        {
            var mission = Object.FindFirstObjectByType<MarsMissionDirector>();
            SculptTerrain(d, AssetDatabase.LoadAssetAtPath<TerrainData>(Landscape + "Ares Basin Terrain.asset"), mission.cargoMission.deliveryZone.transform.position.y);
        }

        static void SculptTerrain(TerrainData data, TerrainData original, float padFloor)
        {
            int n = original.heightmapResolution;
            float[,] heights = original.GetHeights(0, 0, n, n);
            Vector2 mouth = new Vector2(Mouth.x, Mouth.z);
            Vector2 forward = new Vector2(9f, 7f).normalized;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x * data.size.x / (n - 1), z * data.size.z / (n - 1));
                float h = heights[z, x] * data.size.y;
                // Softly grade an apron into the existing basin instead of suspending a slab above it.
                float apron = new Vector2((p.x - Pad.x) / 17f, (p.y - Pad.z) / 14f).magnitude;
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 1.8f, apron));
                h = Mathf.Lerp(h, padFloor - .16f, flat);
                // The final passage rises through an outcrop. Keep a rock cap above its buried half.
                float alongMouth = Vector2.Dot(p - mouth, forward);
                float ridgeEnd = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 32f, alongMouth));
                float ridgeHeight = h;
                if (ridgeEnd > 0f)
                {
                    for (int i = 1; i < Tunnel.Length; i++)
                    {
                        Vector2 a = new Vector2(Tunnel[i - 1].x, Tunnel[i - 1].z);
                        Vector2 b = new Vector2(Tunnel[i].x, Tunnel[i].z);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
                        float d = Vector2.Distance(p, Vector2.Lerp(a, b, t));
                        float blend = 1f - Mathf.SmoothStep(0f, 1f, d / 43f);
                        float relief = 7f * Mathf.PerlinNoise(p.x * .045f, p.y * .045f);
                        float roof = Mathf.Lerp(Tunnel[i - 1].y, Tunnel[i].y, t) + 20f + relief;
                        ridgeHeight = Mathf.Max(ridgeHeight, Mathf.Lerp(h, Mathf.Max(h, roof), blend * ridgeEnd));
                    }
                }
                heights[z, x] = ridgeHeight / data.size.y;
            }
            data.SetHeights(0, 0, heights);
            int hn = original.holesResolution;
            bool[,] holes = original.GetHoles(0, 0, hn, hn);
            Vector2 right = new Vector2(forward.y, -forward.x);
            for (int z = 0; z < hn; z++) for (int x = 0; x < hn; x++)
            {
                Vector2 delta = new Vector2((x + .5f) * data.size.x / hn, (z + .5f) * data.size.z / hn) - mouth;
                // Organic rim instead of a straight-edged patch: a noisy ellipse, longer up the passage than across it.
                float wobble = (Mathf.PerlinNoise(x * .11f, z * .11f) - .5f) * 7f;
                float radius = 24f + wobble;
                float reach = Mathf.Sqrt(Mathf.Pow(Vector2.Dot(delta, right) / radius, 2f)
                    + Mathf.Pow(Vector2.Dot(delta, forward) / (radius + 4f), 2f));
                if (reach < 1f) holes[z, x] = false;
            }
            data.SetHoles(0, 0, holes);
        }

        struct Vertex
        {
            public Vector3 p, n;
            public Vector2 uv;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
                p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t) };
        }

        static Mesh ContinueCave(Mesh source)
        {
            const float cut = 870f;
            var vertices = source.vertices; var normals = source.normals; var uv = source.uv;
            var result = new MeshDraft();
            var edge = new Dictionary<Vector2Int, Vector3>();
            int[] triangles = source.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                var polygon = new List<Vertex>();
                for (int i = 0; i < 3; i++)
                {
                    int index = triangles[t + i];
                    polygon.Add(new Vertex { p = vertices[index], n = normals[index], uv = uv[index] });
                }
                var clipped = new List<Vertex>();
                for (int i = 0; i < 3; i++)
                {
                    Vertex a = polygon[i], b = polygon[(i + 1) % 3];
                    bool insideA = a.p.z <= cut, insideB = b.p.z <= cut;
                    if (insideA) clipped.Add(a);
                    if (insideA != insideB)
                    {
                        Vertex point = Vertex.Lerp(a, b, (cut - a.p.z) / (b.p.z - a.p.z));
                        point.p.z = cut;
                        clipped.Add(point);
                        edge[new Vector2Int(Mathf.RoundToInt(point.p.x * 1000f), Mathf.RoundToInt(point.p.y * 1000f))] = point.p;
                    }
                }
                for (int i = 1; i < clipped.Count - 1; i++) result.Triangle(clipped[0], clipped[i], clipped[i + 1]);
            }
            Vector3 center = Tunnel[0];
            Vector3 connectionA = Mouth - ExitForward * 27f; connectionA.y = Mouth.y - 27f * .25f;
            Vector3 connectionB = Mouth - ExitForward * 18f; connectionB.y = Mouth.y - 18f * .25f;
            Vector3[] passage = { Tunnel[0], Tunnel[1], Tunnel[2], connectionA, connectionB };
            var contour = edge.Values.OrderBy(p => Mathf.Atan2(p.y - center.y, p.x - center.x)).ToArray();
            if (contour.Length < 30) throw new InvalidOperationException("Cave cut contour is incomplete.");
            Vector3[] previous = contour;
            float travelled = 0f;
            for (int leg = 0; leg < passage.Length - 1; leg++)
            {
                for (int step = 1; step <= 6; step++)
                {
                    float t = step / 6f;
                    Vector3 c = Vector3.Lerp(passage[leg], passage[leg + 1], t);
                    Vector3 direction = Vector3.ProjectOnPlane(passage[leg + 1] - passage[leg], Vector3.up).normalized;
                    float angle = Vector3.SignedAngle(Vector3.forward, direction, Vector3.up);
                    float total = (leg + t) / (passage.Length - 1);
                    Quaternion orientation = Quaternion.Euler(0f, Mathf.Lerp(0f, angle, Mathf.Min(1f, total * 4f)), 0f);
                    var next = new Vector3[contour.Length];
                    for (int j = 0; j < contour.Length; j++)
                    {
                        Vector3 offset = contour[j] - center;
                        float a = Mathf.Atan2(offset.y, offset.x);
                        float rough = 1f + .025f * Mathf.Sin(a * 3f + total * 8f) * Mathf.Sin(Mathf.PI * total);
                        Vector3 rounded = Rim(a) * rough;
                        next[j] = c + orientation * Vector3.Lerp(offset, rounded, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, total * 2.5f)));
                    }
                    float advance = Vector3.Distance(passage[leg], passage[leg + 1]) / 6f;
                    for (int j = 0; j < contour.Length; j++)
                    {
                        int k = (j + 1) % contour.Length;
                        result.Quad(previous[j], next[j], next[k], previous[k],
                            new Vector2(j * .12f, travelled * .12f), new Vector2((j + 1) * .12f, (travelled + advance) * .12f));
                    }
                    travelled += advance;
                    previous = next;
                }
            }
            return result.Mesh("Through cave walls and rising exit", true);
        }

        static void MakePortal(Transform parent, Terrain terrain)
        {
            // Subtract the passage from a solid piece of the hillside, like the original entrance.
            // This creates one continuous cliff, overhang and floor, rather than a raised circular lip.
            Mesh outcrop = ExtractOutcrop(terrain);
            var material = MaterialCopy("Weathered exit rock", "Entrance regolith");
            material.CopyPropertiesFromMaterial(AssetDatabase.LoadAssetAtPath<Material>(Landscape + "Entrance regolith.mat"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Landscape + "Ochre dust Albedo.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Landscape + "Ochre dust Normal.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetFloat("_BumpScale", .18f);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Cull", 0f);
            MeshObject("Weathered exit arch and ground seam", parent,
                SaveMesh("Exit rock joined to terrain", outcrop), material, true);
            EditorUtility.SetDirty(material);
        }

        static Vector3 Rim(float a)
        {
            Vector2 ray = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float distance = float.PositiveInfinity;
            for (int i = 0; i < Opening.Length; i++)
            {
                Vector2 p = Opening[i], edge = Opening[(i + 1) % Opening.Length] - p;
                float cross = ray.x * edge.y - ray.y * edge.x;
                if (Mathf.Abs(cross) < .0001f) continue;
                float t = (p.x * edge.y - p.y * edge.x) / cross;
                float u = (p.x * ray.y - p.y * ray.x) / cross;
                if (t > 0f && u >= 0f && u <= 1f) distance = Mathf.Min(distance, t);
            }
            return new Vector3(ray.x * distance, ray.y * distance, 0f);
        }

        static float OpeningDistance(Vector2 p)
        {
            float squared = float.PositiveInfinity; bool inside = false;
            for (int i = 0, j = Opening.Length - 1; i < Opening.Length; j = i++)
            {
                Vector2 a = Opening[j], b = Opening[i], edge = b-a;
                Vector2 closest = a + edge * Mathf.Clamp01(Vector2.Dot(p-a,edge)/edge.sqrMagnitude);
                squared = Mathf.Min(squared,(p-closest).sqrMagnitude);
                if ((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
            }
            return Mathf.Sqrt(squared) * (inside ? -1f : 1f);
        }

        static Mesh ExtractOutcrop(Terrain terrain)
        {
            const int nx=52, ny=48, nz=48;
            const float xmin=-33f, xmax=33f, ymin=12f, ymax=80f, zmin=-27f, zmax=36f;
            Vector3 right=Vector3.Cross(Vector3.up,ExitForward);
            Func<Vector3,float> field = p =>
            {
                Vector3 delta=p-Mouth;
                float x=Vector3.Dot(delta,right), z=Vector3.Dot(delta,ExitForward);
                float centerY=Mouth.y+Mathf.Min(0f,z)*.25f;
                float rockVariation=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-19f,-8f,z));
                float warp=(Mathf.PerlinNoise(x*.19f+17f,z*.15f+35f)-.5f)*.65f*rockVariation;
                float cavity=OpeningDistance(new Vector2(x,p.y-centerY))+warp;
                // Sits just under the terrain: the ground stays terrain (one material across the seam),
                // and only the walls inside the mouth are rock.
                float ground=terrain.SampleHeight(p)+terrain.transform.position.y-.45f;
                return Mathf.Max(p.y-ground,-cavity);
            };
            Func<Vector3,Vector3> gradient = p => new Vector3(
                field(p+Vector3.right*.12f)-field(p-Vector3.right*.12f),
                field(p+Vector3.up*.12f)-field(p-Vector3.up*.12f),
                field(p+Vector3.forward*.12f)-field(p-Vector3.forward*.12f)).normalized;
            int Index(int x,int y,int z) => (z*(ny+1)+y)*(nx+1)+x;
            var positions=new Vector3[(nx+1)*(ny+1)*(nz+1)]; var values=new float[positions.Length];
            for(int z=0;z<=nz;z++) for(int y=0;y<=ny;y++) for(int x=0;x<=nx;x++)
            {
                int i=Index(x,y,z);
                Vector3 p=Mouth+right*Mathf.Lerp(xmin,xmax,x/(float)nx)+ExitForward*Mathf.Lerp(zmin,zmax,z/(float)nz);
                p.y=Mathf.Lerp(ymin,ymax,y/(float)ny); positions[i]=p; values[i]=field(p);
            }
            int[,] tetra={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
            int[,] edges={{0,1},{0,2},{0,3},{1,2},{1,3},{2,3}};
            var draft=new MeshDraft(); var cube=new int[8];var indices=new int[4];
            var points=new List<Vector3>(4);
            for(int z=0;z<nz;z++) for(int y=0;y<ny;y++) for(int x=0;x<nx;x++)
            {
                cube[0]=Index(x,y,z);cube[1]=Index(x+1,y,z);cube[2]=Index(x+1,y+1,z);cube[3]=Index(x,y+1,z);
                cube[4]=Index(x,y,z+1);cube[5]=Index(x+1,y,z+1);cube[6]=Index(x+1,y+1,z+1);cube[7]=Index(x,y+1,z+1);
                bool first=values[cube[0]]<0f;
                if(cube.All(i=>(values[i]<0f)==first)) continue;
                for(int t=0;t<6;t++)
                {
                    for(int k=0;k<4;k++) indices[k]=cube[tetra[t,k]];
                    points.Clear();
                    for(int e=0;e<6;e++)
                    {
                        int a=indices[edges[e,0]],b=indices[edges[e,1]];
                        if((values[a]<0f)==(values[b]<0f)) continue;
                        Vector3 p=Vector3.Lerp(positions[a],positions[b],values[a]/(values[a]-values[b]));
                        if(!points.Any(q=>(p-q).sqrMagnitude<.000001f)) points.Add(p);
                    }
                    if(points.Count<3) continue;
                    Vector3 center=Vector3.zero;foreach(var p in points) center+=p;center/=points.Count;
                    Vector3 normal=gradient(center);
                    Vector3 axis=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right).normalized;
                    Vector3 other=Vector3.Cross(normal,axis);
                    Vector3 origin=center;
                    points.Sort((a,b)=>Mathf.Atan2(Vector3.Dot(a-origin,other),Vector3.Dot(a-origin,axis))
                        .CompareTo(Mathf.Atan2(Vector3.Dot(b-origin,other),Vector3.Dot(b-origin,axis))));
                    // One continuous wrap avoids visible triangular texture-chart boundaries.
                    float Angle(Vector3 p) {
                        Vector3 d=p-Mouth; float z=Vector3.Dot(d,ExitForward);
                        return Mathf.Atan2(p.y-Mouth.y-Mathf.Min(0f,z)*.25f,Vector3.Dot(d,right));
                    }
                    float referenceAngle=Angle(center);
                    Vector2 Surface(Vector3 p) {
                        float angle=referenceAngle+Mathf.DeltaAngle(referenceAngle*Mathf.Rad2Deg,Angle(p)*Mathf.Rad2Deg)*Mathf.Deg2Rad;
                        return new Vector2(angle*(4f/Mathf.PI),Vector3.Dot(p-Mouth,ExitForward)*.065f);
                    }
                    Vertex Make(Vector3 p) => new Vertex {p=p,n=gradient(p),uv=Surface(p)};
                    for(int k=1;k<points.Count-1;k++)
                    {
                        Vertex a=Make(points[0]),b=Make(points[k]),c=Make(points[k+1]);
                        if(Vector3.Dot(Vector3.Cross(b.p-a.p,c.p-a.p),normal)<0f) draft.Triangle(a,c,b);
                        else draft.Triangle(a,b,c);
                    }
                }
            }
            return draft.Mesh("Exit rock joined to terrain");
        }

        static void BuildLandingPad(MarsMissionDirector mission, Transform scenery, Vector3 floor)
        {
            var zone = mission.cargoMission.deliveryZone;
            zone.transform.position = floor;
            zone.name = "Delivery - recovery station beyond cave";
            foreach (Transform child in zone.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var deck = new MeshDraft();
            Vector3[] outline = { new Vector3(-9.5f,0,-9), new Vector3(9.5f,0,-9), new Vector3(12,0,-6.5f),
                new Vector3(12,0,6.5f),new Vector3(9.5f,0,9),new Vector3(-9.5f,0,9),new Vector3(-12,0,6.5f),new Vector3(-12,0,-6.5f) };
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 a = outline[i], b = outline[(i + 1) % outline.Length];
                deck.TopTriangle(Vector3.zero, b, a);
                deck.Quad(a, b, b + Vector3.down * .3f, a + Vector3.down * .3f, Vector2.zero, Vector2.one);
            }
            var mat = MaterialCopy("Dust worn recovery deck", "Launch deck panels");
            mat.SetColor("_BaseColor", new Color(.72f, .62f, .48f));
            mat.SetFloat("_Smoothness", .12f);
            MeshObject("Chamfered landing deck", zone.transform, SaveMesh("Recovery station deck", deck.Mesh("Recovery station deck")), mat, true);
            var hardware = AssetDatabase.LoadAssetAtPath<Material>(Landscape + "Graphite hardware.mat");
            var lamp = MaterialCopy("Recovery approach light", "Ice blue signal");
            lamp.SetColor("_BaseColor", new Color(.65f, .85f, .9f));
            lamp.SetColor("_EmissionColor", new Color(.65f, .85f, .9f) * 2f);
            lamp.EnableKeyword("_EMISSION");
            foreach (Vector3 corner in outline)
            {
                Box("Recessed approach lamp", zone.transform, corner * .94f + Vector3.up * .035f, new Vector3(.6f,.08f,.3f), hardware);
                Box("Light strip", zone.transform, corner * .94f + Vector3.up * .085f, new Vector3(.42f,.025f,.1f), lamp);
            }
            // A small service cabinet establishes scale without blocking either landing bay.
            var equipment = Child(scenery, "Recovery station service cabinet");
            equipment.position = floor + new Vector3(15.5f, -.13f, 5.5f);
            Box("Dust plinth", equipment, Vector3.up * .15f, new Vector3(2.5f,.3f,1.8f), hardware, true);
            Box("Cabinet", equipment, Vector3.up * .9f, new Vector3(1.8f,1.3f,1.1f), hardware, true);
            Box("Status slit", equipment, new Vector3(0f,1.15f,-.57f), new Vector3(.7f,.06f,.03f), lamp);
            for (int i = 0; i < 5; i++) Box("Vent", equipment, new Vector3(0f,.65f+i*.08f,-.57f), new Vector3(1.1f,.025f,.035f), mat);
            var solar = Box("Small service solar panel", equipment, new Vector3(0f,1.75f,.15f), new Vector3(2.4f,.08f,1.7f),
                AssetDatabase.LoadAssetAtPath<Material>(Landscape + "FPV carbon fiber.mat"));
            solar.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
            mission.landingPoint.position = floor;
            mission.assist.routeLanding.position = floor + new Vector3(6.3f,.45f,0f);
            EditorUtility.SetDirty(mat);
            EditorUtility.SetDirty(lamp);
        }

        static void BuildRoute(MarsMissionDirector mission, Vector3 floor)
        {
            var assist = mission.assist;
            Vector3[] existing = assist.routeAfterPickup.Take(23).Select(t => t.position).ToArray();
            var parent = assist.routeBeforePickup[0].parent.parent;
            Transform path = Child(parent, "Through cave - delivery route");
            Transform oldPath = assist.routeAfterPickup[0].parent;
            if (oldPath != path) { oldPath.name = "Archived return route - unused"; oldPath.gameObject.SetActive(false); }
            Transform legacy = parent.Find("With rover");
            if (legacy != null) { legacy.name = "Archived return route - unused"; legacy.gameObject.SetActive(false); }
            ClearChildren(path);
            var points = new List<Vector3>(existing) { new Vector3(578f,9f,852f) };
            points.AddRange(Tunnel.Take(Tunnel.Length - 1));
            points.Add(Mouth);
            points.Add(new Vector3(635f,34f,929f));
            points.Add(new Vector3(654f,31f,934f));
            points.Add(floor + Vector3.up * 13f);
            points.Add(floor + Vector3.up * 3.3f);
            var waypoints = new List<Transform>();
            for (int i = 0; i < points.Count; i++)
            {
                var point = new GameObject($"With rover {i:00}").transform;
                point.SetParent(path); point.position = points[i]; waypoints.Add(point);
            }
            assist.routeAfterPickup = waypoints.ToArray();
            // Preserve enum ordinal 7 for existing scene serialization; its new meaning is forward cave exit.
            mission.zones[6].name = "07 - S5 - air from the far passage";
            mission.zones[6].stage = MarsMissionStage.CaveExit;
            mission.zones[7].name = "08 - S5 - daylight ahead";
            mission.zones[7].transform.position = new Vector3(600f,23f,900f);
            mission.zones[7].stage = MarsMissionStage.CaveExit;
            mission.zones[8].name = "09 - S6 - exit above recovery station";
            mission.zones[8].transform.position = new Vector3(635f,34f,929f);
            mission.zones[8].stage = MarsMissionStage.Landing;
            foreach (var zone in mission.zones) EditorUtility.SetDirty(zone);
            EditorUtility.SetDirty(assist);
            EditorUtility.SetDirty(mission);
        }

        static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            return child;
        }
        static void ClearChildren(Transform root) { foreach (Transform t in root.Cast<Transform>().ToArray()) Object.DestroyImmediate(t.gameObject); }
        static Material MaterialCopy(string name, string source)
        {
            string path = Folder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(AssetDatabase.LoadAssetAtPath<Material>(Landscape + source + ".mat")); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
        static Mesh SaveMesh(string name, Mesh mesh)
        {
            string path = Folder + name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            // CopySerialized alone leaves an existing mesh's GPU buffers stale in the open scene.
            saved.Clear(); saved.indexFormat = mesh.indexFormat;
            saved.vertices = mesh.vertices; saved.normals = mesh.normals; saved.uv = mesh.uv;
            saved.triangles = mesh.triangles; saved.tangents = mesh.tangents; saved.bounds = mesh.bounds;
            saved.UploadMeshData(false);
            Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); return saved;
        }
        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, bool collider)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
        static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        sealed class MeshDraft
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            public void Triangle(Vertex a, Vertex b, Vertex c)
            {
                foreach (Vertex v in new[] { a,b,c }) { triangles.Add(vertices.Count); vertices.Add(v.p); normals.Add(v.n); uv.Add(v.uv); }
            }
            public void TopTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                Func<Vector3, Vertex> top = p => new Vertex { p = p, n = Vector3.up, uv = new Vector2(p.x / 24f + .5f, p.z / 18f + .5f) };
                Triangle(top(a), top(b), top(c));
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 low, Vector2 high, bool worldUv = false)
            {
                Vector3 normal = Vector3.Cross(b-a,c-a).normalized;
                var va = new Vertex {p=a,n=normal,uv=low}; var vb = new Vertex {p=b,n=normal,uv=new Vector2(low.x,high.y)};
                var vc = new Vertex {p=c,n=normal,uv=high}; var vd = new Vertex {p=d,n=normal,uv=new Vector2(high.x,low.y)};
                if (worldUv) { va.uv=SurfaceUv(a); vb.uv=SurfaceUv(b); vc.uv=SurfaceUv(c); vd.uv=SurfaceUv(d); }
                Triangle(va,vb,vc); Triangle(va,vc,vd);
            }
            static Vector2 SurfaceUv(Vector3 p) => new Vector2(p.x*.08f, p.z*.08f+p.y*.06f);
            public Mesh Mesh(string name, bool smooth = false)
            {
                if (smooth)
                {
                    var sums = new Dictionary<Vector3Int,Vector3>();
                    Func<Vector3,Vector3Int> key = p => new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));
                    for(int i=0;i<vertices.Count;i++) { var k=key(vertices[i]); sums[k]=(sums.TryGetValue(k,out var value)?value:Vector3.zero)+normals[i]; }
                    for(int i=0;i<vertices.Count;i++) normals[i]=sums[key(vertices[i])].normalized;
                }
                var mesh = new Mesh { name=name, indexFormat=IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
            }
        }
    }
}


