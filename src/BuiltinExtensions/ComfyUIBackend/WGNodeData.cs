using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Represents data from a node output in <see cref="WorkflowGenerator"/>.</summary>
public class WGNodeData(JArray _path, WorkflowGenerator _gen, string _dataType, T2IModelCompatClass _compat = null)
{
    public static string DT_IMAGE = "IMAGE", DT_LATENT_IMAGE = "LATENT_IMAGE",
        DT_MODEL = "MODEL", DT_TEXTENC = "TEXTENC", DT_VAE = "VAE", DT_AUDIOVAE = "AUDIOVAE",
        DT_VIDEO = "VIDEO", DT_LATENT_VIDEO = "LATENT_VIDEO",
        DT_AUDIO = "AUDIO", DT_LATENT_AUDIO = "LATENT_AUDIO",
        DT_LATENT_AUDIOVIDEO = "LATENT_AUDIOVIDEO";

    /// <summary>Returns true if this holds some form of model (model, textenc, vae, ...).</summary>
    public bool IsAModelType => DataType == DT_MODEL || DataType == DT_VAE || DataType == DT_AUDIOVAE;

    /// <summary>Returns true if this holds latent-encoded media data.</summary>
    public bool IsLatentData => DataType.StartsWith("LATENT_");

    /// <summary>Returns true if this holds raw media data (image, video, audio, ...).</summary>
    public bool IsRawMedia => DataType == DT_IMAGE || DataType == DT_AUDIO || DataType == DT_VIDEO;

    /// <summary>Actual backing path to the node output in the workflow.</summary>
    public JArray Path = _path;

    /// <summary>What type of data is in this. Use the static types available in <see cref="WGNodeData"/>.</summary>
    public string DataType = _dataType;

    /// <summary>If known and relevant (eg latent), what class of models this is compatible with.</summary>
    public T2IModelCompatClass Compat = _compat;

    /// <summary>Returns true if this data has the same compat class as given.</summary>
    public bool IsCompat(T2IModelCompatClass clazz) => Compat is not null && clazz.ID == Compat.ID;

    /// <summary>The width in image space, if known and valid.</summary>
    public int? Width = null;

    /// <summary>The height in image space, if known and valid.</summary>
    public int? Height = null;

    /// <summary>The number of frames in video space, if known and valid.</summary>
    public int? Frames = null;

    /// <summary>The frames per second of a video, if known and valid.</summary>
    public int? FPS = null;

    /// <summary>The backing relevant workflow generator.</summary>
    public WorkflowGenerator Gen = _gen;

    /// <summary>The current relevant user input data.</summary>
    public T2IParamInput UserInput => Gen.UserInput;

    /// <summary>The current relevant workflow supported feature set.</summary>
    public HashSet<string> Features => Gen.Features;

    /// <summary>Create an exact copy of this object.</summary>
    public WGNodeData Duplicate() => (WGNodeData)MemberwiseClone();

    /// <summary>Returns a copy of this node data with a different node path and optional different type.</summary>
    public WGNodeData WithPath(JArray path, string dataType = null)
    {
        WGNodeData dup = Duplicate();
        dup.Path = path;
        dup.DataType = dataType ?? dup.DataType;
        return dup;
    }

    /// <summary>Returns a copy of this node data with a different node path and optional different type.</summary>
    public WGNodeData WithPath(string node, int index, string dataType = null) => WithPath([node, index], dataType);

    /// <summary>Decode the latent data in this object to raw media data. If it is already raw, it will be returned unmodified.</summary>
    /// <param name="vae">The VAE or AudioVAE to decode with.</param>
    /// <param name="wantAudio">True if you want audio data, false if you want image/video data.</param>
    /// <param name="id">Optional node ID override.</param>
    public WGNodeData DecodeLatents(WGNodeData vae, bool wantAudio, string id = null)
    {
        if (IsRawMedia)
        {
            WGAssert(wantAudio == (DataType == DT_AUDIO), $"Data is {DataType} but wantAudio is {wantAudio}, mismatched and therefore failed.");
            return this;
        }
        WGAssert(IsLatentData, $"Cannot decode latents from data of type '{DataType}'.");
        WGAssert(vae is not null, $"Must provide a VAE to decode {DataType} data.");
        if (DataType == DT_LATENT_IMAGE || DataType == DT_LATENT_VIDEO)
        {
            WGAssert(!wantAudio, $"Data is {DataType} but wantAudio is true, mismatched and therefore failed.");
            string decoded = Gen.CreateVAEDecode(vae.Path, Path, canAudioDecode: false, id: id);
            return WithPath([decoded, 0], DT_IMAGE);
        }
        if (DataType == DT_LATENT_AUDIOVIDEO)
        {
            if (IsCompat(T2IModelClassSorter.CompatLtxv2))
            {
                string separated = Gen.CreateNode("LTXVSeparateAVLatent", new JObject()
                {
                    ["av_latent"] = Path
                });
                if (wantAudio)
                {
                    WGNodeData latentAudio = WithPath([separated, 1], DT_LATENT_AUDIO);
                    return latentAudio.DecodeLatents(vae, true, id);
                }
                else
                {
                    WGNodeData latentVideo = WithPath([separated, 0], DT_LATENT_VIDEO);
                    return latentVideo.DecodeLatents(vae, false, id);
                }
            }
            else
            {
                WGAssert(false, $"Cannot decode LATENT_AUDIOVIDEO data from compat class '{Compat}'.");
            }
        }
        if (DataType == DT_LATENT_AUDIO)
        {
            WGAssert(wantAudio, $"Data is {DataType} but wantAudio is false, mismatched and therefore failed.");
            if (IsCompat(T2IModelClassSorter.CompatLtxv2))
            {
                string audioDecoded = Gen.CreateNode("LTXVAudioVAEDecode", new JObject()
                {
                    ["audio_vae"] = vae.Path,
                    ["samples"] = Path
                }, id);
                return WithPath([audioDecoded, 0], DT_AUDIO);
            }
            string decoded = Gen.CreateNode("VAEDecodeAudio", new JObject()
            {
                ["vae"] = vae.Path,
                ["samples"] = Path
            }, id);
            return WithPath([decoded, 0], DT_AUDIO);
        }
        WGAssert(false, $"Unknown latent data type '{DataType}', cannot decode.");
        return null;
    }

    /// <summary>Encode the raw media data in this object to latent data. If it is already latent, it will be returned unmodified.</summary>
    /// <param name="vae">The VAE or AudioVAE to decode with.</param>
    /// <param name="id">Optional node ID override.</param>
    public WGNodeData EncodeToLatent(WGNodeData vae, string id = null)
    {
        if (IsLatentData)
        {
            return this;
        }
        if (DataType == DT_IMAGE || DataType == DT_VIDEO)
        {
            string encoded;
            if (IsCompat(T2IModelClassSorter.CompatCascade))
            {
                encoded = Gen.CreateNode("StableCascade_StageC_VAEEncode", new JObject()
                {
                    ["vae"] = vae.Path,
                    ["image"] = Path,
                    ["compression"] = UserInput.Get(T2IParamTypes.CascadeLatentCompression, 32)
                }, id);
            }
            else if (UserInput.TryGet(T2IParamTypes.VAETileSize, out _) || UserInput.TryGet(T2IParamTypes.VAETemporalTileSize, out _))
            {
                encoded = Gen.CreateNode("VAEEncodeTiled", new JObject()
                {
                    ["vae"] = vae.Path,
                    ["pixels"] = Path,
                    ["tile_size"] = UserInput.Get(T2IParamTypes.VAETileSize, 256),
                    ["overlap"] = UserInput.Get(T2IParamTypes.VAETileOverlap, 64),
                    ["temporal_size"] = UserInput.Get(T2IParamTypes.VAETemporalTileSize, Gen.IsAnyWanModel() ? 9999 : 32),
                    ["temporal_overlap"] = UserInput.Get(T2IParamTypes.VAETemporalTileOverlap, 4)
                }, id);
            }
            else
            {
                encoded = Gen.CreateNode("VAEEncode", new JObject()
                {
                    ["vae"] = vae.Path,
                    ["pixels"] = Path
                }, id);
            }
            return WithPath([encoded, 0], DataType == DT_IMAGE ? DT_LATENT_IMAGE : DT_LATENT_VIDEO);
        }
        if (DataType == DT_AUDIO)
        {
            if (IsCompat(T2IModelClassSorter.CompatLtxv2))
            {
                string encoded = Gen.CreateNode("LTXVAudioVAEEncode", new JObject()
                {
                    ["audio_vae"] = vae.Path,
                    ["audio"] = Path
                }, id);
                return WithPath([encoded, 0], DT_LATENT_AUDIO);
            }
            else
            {
                string encoded = Gen.CreateNode("VAEEncodeAudio", new JObject()
                {
                    ["vae"] = vae.Path,
                    ["audio"] = Path
                }, id);
                return WithPath([encoded, 0], DT_LATENT_AUDIO);
            }
        }
        WGAssert(false, $"Unknown raw media data type '{DataType}', cannot encode to latent.");
        return null;
    }

    /// <summary>Emit nodes to save this data as output. Only works with media or latent media types (latents will be autodecoded using the given VAEs).</summary>
    public string SaveOutput(WGNodeData vae, WGNodeData audioVae, WGNodeData attachAudio = null, string id = null)
    {
        if (DataType == DT_LATENT_IMAGE || DataType == DT_LATENT_VIDEO)
        {
            WGNodeData decoded = DecodeLatents(vae, false);
            return decoded.SaveOutput(vae, audioVae, attachAudio, id);
        }
        if (DataType == DT_LATENT_AUDIO)
        {
            WGNodeData decoded = DecodeLatents(audioVae, true);
            return decoded.SaveOutput(vae, audioVae, attachAudio, id);
        }
        if (DataType == DT_LATENT_AUDIOVIDEO)
        {
            WGNodeData decodedVideo = DecodeLatents(vae, false);
            WGNodeData decodedAudio = attachAudio ?? DecodeLatents(audioVae, true);
            return decodedVideo.SaveOutput(vae, audioVae, decodedAudio, id);
        }
        if (attachAudio is not null)
        {
            if (attachAudio.DataType == DT_LATENT_AUDIO)
            {
                attachAudio = attachAudio.DecodeLatents(audioVae, true);
            }
            WGAssert(attachAudio.DataType == DT_AUDIO, $"Can only attach audio data, but attachAudio is of type {attachAudio.DataType}.");
        }
        WGAssert(IsRawMedia, $"Can only save output from raw media data, but data is of type {DataType}.");
        if (DataType == DT_IMAGE && attachAudio is null)
        {
            if (Features.Contains("comfy_saveimage_ws") && !WorkflowGenerator.RestrictCustomNodes)
            {
                return Gen.CreateNode("SwarmSaveImageWS", new JObject()
                {
                    ["images"] = Path,
                    ["bit_depth"] = UserInput.Get(T2IParamTypes.BitDepth, "8bit")
                }, id);
            }
            else
            {
                return Gen.CreateNode("SaveImage", new JObject()
                {
                    ["filename_prefix"] = $"SwarmUI_{Random.Shared.Next():X4}_",
                    ["images"] = Path
                }, id);
            }
        }
        if (DataType == DT_VIDEO || DataType == DT_IMAGE)
        {
            if (UserInput.Get(T2IParamTypes.VideoBoomerang, false))
            {
                // TODO: Should we really be doing this *here*?!
                // Also arguably audio should boomerang too but that'd probably be weird
                string bounced = Gen.CreateNode("SwarmVideoBoomerang", new JObject()
                {
                    ["images"] = Path
                });
                WGNodeData newNode = WithPath([bounced, 0], DT_VIDEO);
                newNode.Frames = Frames is not null ? Frames * 2 : null;
                return newNode.SaveOutput(vae, audioVae, attachAudio, id);
            }
            return Gen.CreateNode("SwarmSaveAnimationWS", new JObject()
            {
                ["images"] = Path,
                ["fps"] = FPS ?? Gen.Text2VideoFPS(),
                ["lossless"] = false,
                ["quality"] = 95,
                ["method"] = "default",
                ["format"] = UserInput.Get(T2IParamTypes.VideoFormat, "h264-mp4"),
                ["audio"] = attachAudio?.Path
            }, id);
        }
        if (DataType == DT_AUDIO)
        {
            WGAssert(attachAudio is null, $"Cannot attach audio onto other audio.");
            // TODO: SwarmSaveAudio? Better format control instead of just using mp3?
            return Gen.CreateNode("SaveAudioMP3", new JObject()
            {
                ["audio"] = Path,
                ["filename_prefix"] = $"SwarmUI_{Random.Shared.Next():X4}_",
                ["quality"] = "V0"
            }, id);
        }
        WGAssert(false, $"Unknown data type {DataType}, cannot save output.");
        return null;
    }

    /// <summary>Helper, check a condition and throw a <see cref="SwarmReadableErrorException"/> if it is wrong.</summary>
    public static void WGAssert(bool condition, string message)
    {
        if (!condition)
        {
            Logs.Debug($"Stack trace for WGAssert failure:\n{Environment.StackTrace}");
            throw new SwarmReadableErrorException($"Workflow generator failed from mishandling data: {message}");
        }
    }
}
