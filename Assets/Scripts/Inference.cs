using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Sentis;
using Unity.Collections;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class Inference : MonoBehaviour
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

    public ModelAsset model_base;
    public ModelAsset model_tokenize;
    [FormerlySerializedAs("Instruments")]
    public InstrumentSet instruments = InstrumentSet.PopRock;

    IWorker engine_base, engine_tokenize;
    IBackend backend;

    public MidiTokenizer tokenizer { get; } = new();

    // tokenizer
    int max_token_seq = 8;
    int pad_id = 0;
    int bos_id = 1;
    int eos_id = 2;
    Dictionary<string, List<string>> events = new Dictionary<string, List<string>>();
    Dictionary<int, string> id_events = new Dictionary<int, string>();

    public int max_len = 32;
    public float temp = 1.0f;
    public float top_p = 0.98f;
    public int top_k = 20;
    public bool disable_control_change = true;
    public bool gpu_backend = false;

    bool m_KeepGoing;
    
    public event Action<int> onTokenGenerated;

    void Start()
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
        
        var workerType = gpu_backend ? BackendType.GPUCompute : BackendType.CPU;
        engine_base = WorkerFactory.CreateWorker(workerType, modelBase2);

        var modelTokenize = ModelLoader.Load(model_tokenize);
        var modelTokenize2 = Functional.Compile(
            forward: inputs =>
            {
                var logits = FunctionalTensor.FromModel(modelTokenize, inputs)[0];
                logits = logits[.., ^ 1, ..];
                var scores = Functional.Softmax(logits / temp, -1);

                //var sample = Functional.Multinomial(scores * top_p, 1); // is correct?

                // var probs_sort = Functional.TopK(scores, top_k, -1);
                return new[] { scores };

                // return new[] { Functional.ArgMax(scores, -1) };
            },
            InputDef.FromModel(modelTokenize)
        );
        engine_tokenize = WorkerFactory.CreateWorker(workerType, modelTokenize2);
        backend = gpu_backend ? new GPUComputeBackend() : new CPUBackend();

        // Generate();
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

    void Update() { }

    public async Task<int[][]> Generate(CancellationToken cancellationToken)
    {
        Debug.Log($"Starting generation {max_len}");

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

        var mask = new float[tokenizer.vocab_size];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = 1f;
        }

        while (cur_len < max_len)
        {
            bool end = cancellationToken.IsCancellationRequested;

            engine_base.Execute(input_tensor);
            var hidden_out = engine_base.PeekOutput();

            List<int> token_list = new List<int>();

            string event_name = "";
            for (int i = 0; i < max_token_seq; i++)
            {
                using TensorInt next_token_seq = new TensorInt(new TensorShape(1, i), token_list.ToArray());

                if (i == 0)
                {
                    if (disable_patch_change)
                    {
                        mask[tokenizer.event_ids["patch_change"]] = 0;
                    }
                    if (disable_control_change)
                        mask[tokenizer.event_ids["control_change"]] = 0;
                }
                else if (disable_channels.Length > 0)
                {
                    var param_name = MidiTokenizer.events[event_name][i - 1];
                    if (param_name == "channel")
                    {
                        var mask_ids = tokenizer.parameter_ids[param_name];
                        foreach (var id in mask_ids)
                        {
                            if (disable_channels.Contains(id))
                            {
                                mask[id] = 0;
                            }
                        }
                    }
                }

                engine_tokenize.SetInput("input_0", hidden_out);
                engine_tokenize.SetInput("input_1", next_token_seq);
                engine_tokenize.Execute();
                var scores = engine_tokenize.PeekOutput() as TensorFloat;

                using var maskTensor = new TensorFloat(scores.shape, mask);
                using var maskedScores = TensorFloat.AllocNoData(scores.shape);
                backend.Mul(scores, maskTensor, maskedScores);

                await maskedScores.CompleteOperationsAndDownloadAsync();
                var scoresArray = maskedScores.ToReadOnlyArray();
                var eid = SampleTopPK(scoresArray, top_p, top_k);

                // var index_out = engine_tokenize.PeekOutput();
                // var index_array = index_out.dataOnBackend.Download<int>(1);
                // var eid = index_array[0]; next_token_seq.Dispose(); index_array.Dispose();

                token_list.Add(eid);
                if (i == 0)
                {
                    Debug.Log($"{cur_len}: {eid}");
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

            // pad if earlyout
            TensorInt token_seq = TensorInt.AllocZeros(new TensorShape(1, 1, max_token_seq));
            for (int jj = 0; jj < token_list.Count; jj++)
                token_seq[jj] = token_list[jj];
            var concat_tensor = TensorInt.AllocNoData(new TensorShape(1, input_tensor.shape[1] + 1, input_tensor.shape[2]));
            backend.Concat(new[] { input_tensor, token_seq }, concat_tensor, 1);
            input_tensor.Dispose();
            token_seq.Dispose();
            input_tensor = concat_tensor;
            cur_len += 1;
            onTokenGenerated?.Invoke(cur_len);
            if (end)
                break;
        }

        await input_tensor.CompleteOperationsAndDownloadAsync();

        // var output = input_tensor.ToReadOnlyArray();
        var results = TensorToArray(input_tensor);
        // Debug.Log(input_tensor.shape);
        input_tensor.Dispose();

        return results;
    }

    int SampleTopPK(float[] probs, float p, int k)
    {
        var probsIndices = probs.Select((value, index) => (value, index));
        var sortedProbsIndices = probsIndices.OrderByDescending(e => e.value).Take(k).ToArray();
        var probsCumSum = new float[sortedProbsIndices.Length];
        probsCumSum[0] = sortedProbsIndices[0].value;
        for (int i = 1; i < sortedProbsIndices.Length; i++)
        {
            probsCumSum[i] = probsCumSum[i - 1] + sortedProbsIndices[i].value;
        }

        for (int i = 0; i < sortedProbsIndices.Length; i++)
        {
            if (probsCumSum[i] - sortedProbsIndices[i].value > p)
            {
                sortedProbsIndices[i].value = 0;
            }

            // else if (i > k - 1)
            // {
            //     sortedProbsIndices[i].value = 0;
            // }
        }

        var sumProbs = sortedProbsIndices.Sum(e => e.value);
        for (int i = 0; i < sortedProbsIndices.Length; i++)
        {
            sortedProbsIndices[i].value /= sumProbs;
        }

        return RandomChoice(sortedProbsIndices, sortedProbsIndices.Select(e => e.value).ToArray()).index;
    }

    T RandomChoice<T>(IList<T> items, IList<float> probs)
    {
        var cdf = new float[probs.Count];
        cdf[0] = probs[0];
        for (int i = 1; i < probs.Count; i++)
        {
            cdf[i] = cdf[i - 1] + probs[i];
        }

        var r = Random.value;
        for (int i = 0; i < cdf.Length; i++)
        {
            if (r < cdf[i])
            {
                return items[i];
            }
        }

        return default;
    }

    private void OnDestroy()
    {
        engine_base.Dispose();
        engine_tokenize.Dispose();
        backend.Dispose();
    }
}
