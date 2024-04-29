using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Unity.Sentis;

// Use alex/hw2024-midi until that lands on dev
// 32: 3.8s
// 64: 9.1s
// 128: 24s
// 256: 65s
// ~ linear
// await to not block main thread
public class Inference2 : MonoBehaviour
{
    public enum InstrumentSet
    {
        None,
        Piano,
        Orchestra,
        PopRock
    }

    static readonly Dictionary<InstrumentSet, int[]> k_PresetInputs = new()
    {
        { InstrumentSet.None, Array.Empty<int>() },
        {
            InstrumentSet.Piano, new[]
            {
                1, 0, 0, 0, 0, 0, 0, 0,
                4, 7, 135, 2199, 2327, 2599, 0, 0
            }
        },
        {
            InstrumentSet.Orchestra, new[]
            {
                1,    0,    0,    0,    0,    0,    0,    0,
                4,    7,  135, 2199, 2327, 2639,    0,    0,
                4,    7,  135, 2200, 2328, 2640,    0,    0,
                4,    7,  135, 2201, 2329, 2641,    0,    0,
                4,    7,  135, 2202, 2330, 2642,    0,    0,
                4,    7,  135, 2203, 2331, 2655,    0,    0,
                4,    7,  135, 2204, 2332, 2659,    0,    0,
                4,    7,  135, 2205, 2333, 2660,    0,    0,
                4,    7,  135, 2206, 2334, 2672,    0,    0,
                4,    7,  135, 2207, 2335, 2671,    0,    0,
                4,    7,  135, 2208, 2337, 2657,    0,    0,
                4,    7,  135, 2209, 2338, 2656,    0,    0,
                4,    7,  135, 2210, 2339, 2646,    0,    0,
                4,    7,  135, 2211, 2336, 2647,    0,    0
            }
        },
        {
            InstrumentSet.PopRock, new[]
            {
                1, 0, 0, 0, 0, 0, 0, 0,
                4, 7, 135, 2199, 2327, 2623, 0, 0,
                4, 7, 135, 2200, 2328, 2624, 0, 0,
                4, 7, 135, 2201, 2329, 2625, 0, 0,
                4, 7, 135, 2202, 2330, 2626, 0, 0,
                4, 7, 135, 2203, 2331, 2627, 0, 0,
                4, 7, 135, 2204, 2332, 2628, 0, 0,
                4, 7, 135, 2205, 2333, 2629, 0, 0,
                4, 7, 135, 2206, 2334, 2632, 0, 0,
                4, 7, 135, 2207, 2336, 2599, 0, 0
            }
        }
    };

    static readonly Dictionary<InstrumentSet, int[]> k_ChannelMasks = new()
    {
        { InstrumentSet.None, Array.Empty<int>() },
        { InstrumentSet.Piano, new[] { 2328, 2329, 2330, 2331, 2332, 2333, 2334, 2335, 2336, 2337, 2338, 2339, 2340, 2341, 2342 } },
        { InstrumentSet.Orchestra, new[] { 2340, 2341, 2342 } },
        { InstrumentSet.PopRock, new[] { 2335, 2337, 2338, 2339, 2340, 2341, 2342 } }
    };

    public ModelAsset model_base, model_tokenize;
    IWorker engine_base, engine_tokenize;
    IBackend backend;
    
    public InstrumentSet instruments = InstrumentSet.PopRock;
    public MidiTokenizer tokenizer { get; } = new();

    public int max_len = 256;
    public float temp = 1.0f;
    public float top_p = 0.98f;
    public int top_k = 20;
    public bool disable_control_change = true;
    public bool gpu_backend = true;

    bool m_KeepGoing;
    public event Action<int> onTokenGenerated;
    
    // TensorInt input_tensor;

    // tokenizer
    int max_token_seq = 8;
    int pad_id = 0;
    int bos_id = 1;
    int eos_id = 2;
    Dictionary<string, List<string>> events = new Dictionary<string, List<string>>();
    Dictionary<int, string> id_events = new Dictionary<int, string>();

    public void Start()
    {
        id_events[3] = "note";
        id_events[4] = "patch_change";
        id_events[5] = "control_change";
        id_events[6] = "set_tempo";
        events["note"] = new List<string>() { "time1", "time2", "track", "duration", "channel", "pitch", "velocity" };
        events["patch_change"] = new List<string>() { "time1", "time2", "track", "channel", "patch" };
        events["control_change"] = new List<string>() { "time1", "time2", "track", "channel", "controller", "value" };
        events["set_tempo"] = new List<string>() { "time1", "time2", "track", "bpm" };


        var modelBase = ModelLoader.Load(model_base);
        var modelBase2 = Functional.Compile(
            forward: input =>
            {
                var hidden = FunctionalTensor.FromModel(modelBase, new[] { input })[0];
                return hidden[.., ^ 1];
            },
            InputDef.FromModelInput(modelBase.inputs[0])
        );

        var modelTokenize = ModelLoader.Load(model_tokenize);
        var modelTokenize2 = Functional.Compile(
            forward: inputs =>
            {
                var logits = FunctionalTensor.FromModel(modelTokenize, new[] { inputs[0], inputs[1] })[0];
                logits = logits[.., ^ 1, ..];
                var scores = Functional.Softmax(logits / temp, -1);

                //return new[] { Functional.ArgMax(scores, -1) };
                scores *= inputs[2];

                var probsAndIndices = Functional.TopK(scores, top_k, -1);
                var sortedProbs = probsAndIndices[0];
                var sortedIndices = probsAndIndices[1];
                var cumSum = Functional.CumSum(sortedProbs, -1);

                var mask = (cumSum - sortedProbs) <= top_p;
                sortedProbs *= mask;
                sortedProbs /= Functional.ReduceSum(sortedProbs, -1);

                var indices = Functional.RandomChoice(sortedProbs);

                return new[] { Functional.Gather(sortedIndices, -1, indices) };
            },
            new[]
            {
                InputDef.FromModelInput(modelTokenize.inputs[0]), InputDef.FromModelInput(modelTokenize.inputs[1]),
                new InputDef(DataType.Float, new TensorShape(1, tokenizer.vocab_size))
            }
        );

        engine_base = WorkerFactory.CreateWorker(BackendType.GPUCompute, modelBase2);
        engine_tokenize = WorkerFactory.CreateWorker(BackendType.GPUCompute, modelTokenize2);
        backend = new GPUComputeBackend();

        // input_tensor = TensorInt.AllocZeros(new TensorShape(1, 1, max_token_seq));
        // input_tensor[0] = bos_id;

        // await GenerateMIDITokens();
        
        // Debug.Log(input_tensor.shape);
        // Debug.Log(input_tensor[31, 0]);
        // Debug.Log(input_tensor[31, 1]);
        Debug.Log(Time.time - startTime);
    }


    public float startTime = 0.0f;
    public async Awaitable<int[][]> Generate(CancellationToken cancellationToken)
    {
        
        TensorInt input_tensor;
        var presetInputs = k_PresetInputs[instruments];
        var disable_channels = k_ChannelMasks[instruments];
        bool disable_patch_change = false;

        int cur_len = 1;
        if (presetInputs.Length > 0)
        {
            // Start with patch events
            TensorShape shape = new TensorShape(1, presetInputs.Length / max_token_seq, max_token_seq);
            input_tensor = new TensorInt(shape, presetInputs);
            disable_patch_change = true;
            cur_len = input_tensor.shape[1];
        }
        else
        {
            // Start with empty tensor
            input_tensor = TensorInt.AllocZeros(new TensorShape(1, 1, max_token_seq));
            input_tensor[0] = bos_id;
        }
        
        startTime = Time.time;
        
        var eventNameMask = new float[tokenizer.vocab_size];
        for (int i = 0; i < eventNameMask.Length; i++)
        {
            eventNameMask[i] = 1f;
        }
        
        // default mask is all 1
        using var defaultMaskTensor = new TensorFloat(new TensorShape(1, tokenizer.vocab_size), eventNameMask);
        
        // disabling patch_change fixes the instruments 
        if (disable_patch_change)
        {
            eventNameMask[tokenizer.event_ids["patch_change"]] = 0;
        }
        if (disable_control_change)
        {
            eventNameMask[tokenizer.event_ids["control_change"]] = 0;
        }
        using var eventMaskTensor = new TensorFloat(new TensorShape(1, tokenizer.vocab_size), eventNameMask);

        // Disable events that drive the specified channels (part of instrument selection).
        var channelMask = new float[tokenizer.vocab_size];
        for (int i = 0; i < channelMask.Length; i++)
        {
            channelMask[i] = 1f;
        }

        if (disable_channels.Length > 0)
        {
            var mask_ids = tokenizer.parameter_ids["channel"];
            foreach (var id in mask_ids)
            {
                if (disable_channels.Contains(id))
                {
                    channelMask[id] = 0;
                }
            }
        }

        using var channelMaskTensor = new TensorFloat(new TensorShape(1, tokenizer.vocab_size), channelMask);

        while (cur_len < max_len)
        {
            bool end = false;
            engine_base.Execute(input_tensor);
            var hidden_out = engine_base.PeekOutput();

            List<int> token_list = new List<int>();
            string event_name = "";
            for (int i = 0; i < max_token_seq; i++)
            {
                TensorFloat mask;
                if (i == 0)
                {
                    mask = eventMaskTensor;
                }
                else
                {
                    var param_name = events[event_name][i - 1];
                    mask = param_name == "channel" ? channelMaskTensor : defaultMaskTensor;
                }
                
                using TensorInt next_token_seq = new TensorInt(new TensorShape(1, i), token_list.ToArray());
            
                engine_tokenize.SetInput("input_0", hidden_out);
                engine_tokenize.SetInput("input_1", next_token_seq);
                engine_tokenize.SetInput("input_2", mask);
                engine_tokenize.Execute();
            
                var index_array = engine_tokenize.PeekOutput() as TensorInt;

                await index_array.CompleteOperationsAndDownloadAsync();
                int eid = index_array[0];
                token_list.Add(eid);
                if (i == 0)
                {
                    if (eid == eos_id)
                    {
                        end = true;
                        break;
                    }
                    event_name = id_events[eid];
                }
                else
                {
                    if (events[event_name].Count == i)
                        break;
                }
            }
            TensorInt token_seq = TensorInt.AllocZeros(new TensorShape(1, 1, max_token_seq));
            for (int jj = 0; jj < token_list.Count; jj++)
                token_seq[jj] = token_list[jj];
            var concat_tensor = TensorInt.AllocNoData(new TensorShape(1, input_tensor.shape[1] + 1, input_tensor.shape[2]));
            backend.Concat(new[] { input_tensor, token_seq }, concat_tensor, 1);
            input_tensor.Dispose();
            token_seq.Dispose();
            input_tensor = concat_tensor;
            cur_len++;
            if (end || cancellationToken.IsCancellationRequested)
                break;
            onTokenGenerated?.Invoke(cur_len);
        }
        Debug.Log(cur_len);
        await input_tensor.CompleteOperationsAndDownloadAsync();
        var results = TensorToArray(input_tensor);
        input_tensor.Dispose();
        return results;
    }

    int[][] TensorToArray(TensorInt tensor)
    {
        var data = tensor.ToReadOnlySpan();
        var arr = new int[tensor.shape[1]][];
        for (int i = 0; i < tensor.shape[1]; i++)
        {
            var tokenStart = i * tensor.shape[2];
            var tokenEnd = tokenStart + tensor.shape[2];
            arr[i] = data[tokenStart..tokenEnd].ToArray();
        }

        return arr;
    }

    private void OnDestroy()
    {
        engine_base.Dispose();
        engine_tokenize.Dispose();
        // input_tensor.Dispose();
        backend.Dispose();
    }
}
