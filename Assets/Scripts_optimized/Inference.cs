using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;

// Use alex/hw2024-midi until that lands on dev
// 32: 3.8s
// 64: 9.1s
// 128: 24s
// 256: 65s
// ~ linear
// await to not block main thread
public class Inference : MonoBehaviour
{
    public ModelAsset model_base, model_tokenize;
    IWorker engine_base, engine_tokenize;
    IBackend backend;

    public int max_len = 256;
    public float temp = 1.0f;
    public float top_p = 0.98f;
    public int top_k = 20;

    TensorInt input_tensor;

    // tokenizer
    int max_token_seq = 8;
    int pad_id = 0;
    int bos_id = 1;
    int eos_id = 2;
    Dictionary<string, List<string>> events = new Dictionary<string, List<string>>();
    Dictionary<int, string> id_events = new Dictionary<int, string>();

    public async Awaitable Start()
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
                var logits = FunctionalTensor.FromModel(modelTokenize, inputs)[0];
                logits = logits[.., ^ 1, ..];
                var scores = Functional.Softmax(logits / temp, -1);

                //return new[] { Functional.ArgMax(scores, -1) };


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
            InputDef.FromModel(modelTokenize)
        );

        engine_base = WorkerFactory.CreateWorker(BackendType.GPUCompute, modelBase2);
        engine_tokenize = WorkerFactory.CreateWorker(BackendType.GPUCompute, modelTokenize2);
        backend = new GPUComputeBackend();

        input_tensor = TensorInt.AllocZeros(new TensorShape(1, 1, max_token_seq));
        input_tensor[0] = bos_id;

        await GenerateMIDITokens();
        
        Debug.Log(input_tensor.shape);
        Debug.Log(input_tensor[31, 0]);
        Debug.Log(input_tensor[31, 1]);
        Debug.Log(Time.time - startTime);
    }


    public float startTime = 0.0f;
    private async Awaitable<bool> GenerateMIDITokens()
    {
        startTime = Time.time;
        int cur_len = 1;
        while (cur_len < max_len)
        {
            bool end = false;
            engine_base.Execute(input_tensor);
            var hidden_out = engine_base.PeekOutput();

            List<int> token_list = new List<int>();
            string event_name = "";
            for (int i = 0; i < max_token_seq; i++)
            {
                using TensorInt next_token_seq = new TensorInt(new TensorShape(1, i), token_list.ToArray());
            
                engine_tokenize.SetInput("input_0", hidden_out);
                engine_tokenize.SetInput("input_1", next_token_seq);
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
            if (end)
                break;
        }
        Debug.Log(cur_len);
        return await input_tensor.CompleteOperationsAndDownloadAsync();
    }

    private void OnDestroy()
    {
        engine_base.Dispose();
        engine_tokenize.Dispose();
        input_tensor.Dispose();
        backend.Dispose();
    }
}
