#if UNITY_EDITOR
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class ReaniIKSurfacePainter
{
    private CancellationTokenSource normalsCancellation;
    public string NormalsStatus { get; private set; }
    public bool NormalsBusy => normalsCancellation != null;
    private void DrawComfyNormalsControls()
    {
        using(new EditorGUI.DisabledScope(NormalsBusy || !entry.effectsEnabled))
            if(GUILayout.Button("Normals via Comfy — selected")) GenerateComfyNormals(false);
        using(new EditorGUI.DisabledScope(NormalsBusy))
            if(GUILayout.Button("Normals via Comfy — all checked")) GenerateComfyNormals(true);
        if(NormalsBusy && GUILayout.Button("Cancel normals generation")) normalsCancellation.Cancel();
        if(!string.IsNullOrEmpty(NormalsStatus)) EditorGUILayout.LabelField(NormalsStatus,EditorStyles.wordWrappedLabel);
    }
    public async void GenerateComfyNormals(bool allChecked)
    {
        if(NormalsBusy || !maps) return;
        SaveBodyCanvas(); Save();
        var library=maps;
        var targets=(allChecked ? sprites.Select(s=>ReaniIKSurfaceAuthoring.EnsureEntry(library,s)) : new[]{entry}).Where(e=>e.effectsEnabled).ToArray();
        normalsCancellation=new CancellationTokenSource();
        NormalsStatus="Connecting to ComfyUI…";
        var cancel=normalsCancellation.Token;
        try
        {
            using(var client=new HttpClient { BaseAddress=new Uri("http://127.0.0.1:8188/"), Timeout=TimeSpan.FromMinutes(20) })
            {
                // This is the same estimator used by Krita AI Diffusion's normal control.
                var info=JObject.Parse(await client.GetStringAsync("object_info/BAE-NormalMapPreprocessor"));
                if(info["BAE-NormalMapPreprocessor"]==null) throw new Exception("ComfyUI needs BAE-NormalMapPreprocessor (ControlNet Aux). Start/enable that node first.");
                for(int i=0;i<targets.Length;i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var target=targets[i];
                    if (!target.effectsEnabled) continue;
                    NormalsStatus=$"Comfy normals {i+1}/{targets.Length}: {target.sprite.name}"; Repaint();
                    byte[] input;
                    var image=ReaniIKSurfaceAuthoring.Extract(target.sprite);
                    try
                    {
                        var colors=image.GetPixels();
                        for(int k=0;k<colors.Length;k++)
                        {
                            float alpha=colors[k].a;
                            colors[k]=Color.Lerp(new Color(.5f,.5f,.5f,1),colors[k].gamma,alpha); colors[k].a=1;
                        }
                        image.SetPixels(colors); image.Apply(); input=image.EncodeToPNG();
                    }
                    finally { DestroyImmediate(image); }
                    string inputName;
                    using(var form=new MultipartFormDataContent())
                    {
                        form.Add(new ByteArrayContent(input),"image","reanimator_normals_"+Guid.NewGuid().ToString("N")+".png");
                        using(var upload=await client.PostAsync("upload/image",form,cancel))
                        { upload.EnsureSuccessStatusCode(); var data=JObject.Parse(await upload.Content.ReadAsStringAsync()); inputName=(string)data["name"]; var sub=(string)data["subfolder"]; if(!string.IsNullOrEmpty(sub))inputName=sub+"/"+inputName; }
                    }
                    if (!target.effectsEnabled) continue;
                    var graph=new JObject {
                        ["1"]=new JObject { ["class_type"]="LoadImage", ["inputs"]=new JObject { ["image"]=inputName } },
                        ["2"]=new JObject { ["class_type"]="BAE-NormalMapPreprocessor", ["inputs"]=new JObject { ["image"]=new JArray("1",0), ["resolution"]=512 } },
                        ["3"]=new JObject { ["class_type"]="SaveImage", ["inputs"]=new JObject { ["images"]=new JArray("2",0), ["filename_prefix"]="Reanimator/Normals/surface" } }
                    };
                    string id;
                    using(var response=await client.PostAsync("prompt",new StringContent(new JObject { ["prompt"]=graph }.ToString(),Encoding.UTF8,"application/json"),cancel))
                    { string body=await response.Content.ReadAsStringAsync(); if(!response.IsSuccessStatusCode)throw new Exception(body); id=(string)JObject.Parse(body)["prompt_id"]; }
                    JObject done=null;
                    var deadline=DateTime.UtcNow.AddMinutes(20);
                    while(DateTime.UtcNow<deadline)
                    {
                        await Task.Delay(500,cancel);
                        var history=JObject.Parse(await client.GetStringAsync("history/"+Uri.EscapeDataString(id)));
                        done=history[id] as JObject;
                        if((string)done?["status"]?["status_str"]=="error") throw new Exception("Comfy inference failed: "+done["status"]?["messages"]);
                        if(done?["outputs"]?["3"]?["images"]?[0]!=null) break;
                        done=null;
                    }
                    if(done==null)throw new TimeoutException("Comfy normal generation timed out.");
                    var output=done["outputs"]["3"]["images"][0];
                    string url="view?filename="+Uri.EscapeDataString((string)output["filename"])+"&subfolder="+Uri.EscapeDataString((string)output["subfolder"]??"")+"&type=output";
                    var bytes=await client.GetByteArrayAsync(url);
                    cancel.ThrowIfCancellationRequested();
                    if(!this || maps!=library)throw new OperationCanceledException();
                    if(!target.effectsEnabled) continue;
                    var normal=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                    try
                    {
                        if(!normal.LoadImage(bytes))throw new Exception("Comfy returned an invalid normal image.");
                        var texture=target.normalAndBlush;
                        var colors=texture.GetPixels();
                        Undo.RegisterCompleteObjectUndo(texture,"Generate Comfy surface normals");
                        for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                        {
                            int k=y*texture.width+x; var n=normal.GetPixelBilinear((x+.5f)/texture.width,(y+.5f)/texture.height);
                            // BAE's camera-facing convention has X pointing image-left.
                            // Sprite tangent normals use X right, Y up, Z outward.
                            colors[k]=new Color(1-n.r,n.g,n.b,colors[k].a);
                        }
                        texture.SetPixels(colors); texture.Apply();
                        target.normalAndBlush=ReaniIKSurfaceAuthoring.WriteTexture(AssetDatabase.GetAssetPath(texture),texture);
                        foreach(var s in bodySurfaces)if(s.entry==target){s.mask=target.normalAndBlush.GetPixels();UpdateBodyOverlay(s);}
                        if(entry==target){pixels=target.normalAndBlush.GetPixels();UpdateOverlay();}
                    }
                    finally { DestroyImmediate(normal); }
                    EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
                }
                NormalsStatus=$"Comfy normals ready: {targets.Length} sprites. Redness preserved.";
            }
        }
        catch(OperationCanceledException){NormalsStatus="Normals generation cancelled. Completed sprites retained.";}
        catch(Exception e){NormalsStatus="Comfy normals: "+e.Message; Debug.LogWarning(NormalsStatus);}
        finally { normalsCancellation.Dispose(); normalsCancellation=null; if(this)Repaint(); }
    }
}
#endif
