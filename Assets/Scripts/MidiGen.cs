using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MidiPlayerTK;
using UnityEngine;

// using UnityEngine.UI;

[RequireComponent(typeof(Inference))]
[RequireComponent(typeof(AudioSource))]
public class MidiGen : MonoBehaviour
{
    Inference m_Inference;
    
    [SerializeField]
    MidiStreamPlayer m_MidiPlayer;
    
    public bool IsGenerating { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool CanPlay => m_PlayQueues.Count > 0;
    
    public int CurrentGenerationLength { get; private set; }
    
    public int MaxLength => m_Inference.max_len;

    public event Action<MPTKEvent> OnNotePlayed;
    
    public event Action<MPTKEvent> OnNoteGenerated;
    
    public event Action<long> OnPlayTimeChanged;
    
    long m_CurrentPlayTime;
    
    readonly List<Queue<MPTKEvent>> m_PlayQueues = new();

    float m_Bpm = 120;
    double m_StartTick;

    readonly ConcurrentQueue<MPTKEvent> m_PlayedNotes = new();

    CancellationTokenSource m_CancellationTokenSource;

    // Start is called before the first frame update
    void Start()
    {
        m_Inference = GetComponent<Inference>();
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!IsPlaying) return;
        
        bool hasEvents = false;
        var currentTicks = SecondsToTicks(AudioSettings.dspTime - m_StartTick);
        m_CurrentPlayTime = TicksToMilliseconds(currentTicks);
        foreach (var queue in m_PlayQueues)
        {
            if (queue.Count == 0) continue;

            hasEvents = true;
            while (queue.Count > 0 && queue.Peek().Tick < currentTicks)
            {
                var midiEvent = queue.Dequeue();
                m_MidiPlayer.MPTK_PlayEvent(midiEvent);
                if (midiEvent.Command is MPTKCommand.NoteOn)
                {
                    m_PlayedNotes.Enqueue(midiEvent);
                }
            }
        }

        if (!hasEvents)
        {
            Debug.Log("Done playing");
            IsPlaying = false;
        }
    }

    void Update()
    {
        while (m_PlayedNotes.TryDequeue(out var midiEvent))
        {
            OnNotePlayed?.Invoke(midiEvent);
        }

        if (IsPlaying)
        {
            OnPlayTimeChanged?.Invoke(m_CurrentPlayTime);
        }
    }
    
    public void CancelGeneration()
    {
        if (IsGenerating)
        {
            m_CancellationTokenSource.Cancel();
        }
    }

    public void Play()
    {
        if (IsPlaying) return;
        
        // QueueEvents(m_Inference.tokenizer.detokenize(m_LatestGeneration).Item1);
        
        IsPlaying = true;
        m_StartTick = AudioSettings.dspTime;
        Debug.Log("Start playing");
    }

    void QueueEvents(List<List<MidiTokenizer.Event>> events)
    {
        for (int trackIndex = 0; trackIndex < events.Count; trackIndex++)
        {
            while (m_PlayQueues.Count <= trackIndex)
            {
                m_PlayQueues.Add(new Queue<MPTKEvent>());
            }

            var track = events[trackIndex];
            foreach (var ev in track)
            {
                var midiEvent = new MPTKEvent
                {
                    // Not used by library, but we use this to keep track of when to trigger the event
                    Tick = ev.parameters[0]
                };
                if (ev.name == "note")
                {
                    midiEvent.Command = MPTKCommand.NoteOn;
                    midiEvent.Duration = TicksToMilliseconds(ev.parameters[1]);
                    midiEvent.Channel = ev.parameters[2];
                    midiEvent.Value = ev.parameters[3];
                    midiEvent.Velocity = ev.parameters[4];

                    Debug.Log($"note: {midiEvent.Channel}, {midiEvent.Value}, {midiEvent.Tick}");
                    
                    m_PlayQueues[trackIndex].Enqueue(midiEvent);
                    OnNoteGenerated?.Invoke(midiEvent);
                }
                else if (ev.name == "patch_change")
                {
                    midiEvent.Command = MPTKCommand.PatchChange;
                    midiEvent.Channel = ev.parameters[1];
                    midiEvent.Value = ev.parameters[2];

                    Debug.Log($"patch_change: {midiEvent.Channel}, {midiEvent.Value}");
                }
                else if (ev.name == "control_change")
                {
                    midiEvent.Command = MPTKCommand.ControlChange;
                    midiEvent.Channel = ev.parameters[1];
                    midiEvent.Controller = (MPTKController)ev.parameters[2];
                    midiEvent.Value = ev.parameters[3];
                    
                    Debug.Log($"control_change: {midiEvent.Channel}, {midiEvent.Controller}, {midiEvent.Value}");
                }
                else if (ev.name == "set_tempo")
                {
                    var tempo = MidiTokenizer.tempo2bpm(ev.parameters[1]);
                    if (tempo > 0)
                    {
                        m_Bpm = MidiTokenizer.tempo2bpm(ev.parameters[1]);
                        Debug.Log($"set_tempo: {m_Bpm}");
                    }
                }
                m_PlayQueues[trackIndex].Enqueue(midiEvent);
            }
        }
    }

    public long TicksToMilliseconds(long ticks)
    {
        var beats = (double)ticks / MidiTokenizer.ticks_per_beat;
        var minutes = beats / m_Bpm;
        return (long)(minutes * 60_000);
    }

    long SecondsToTicks(double seconds)
    {
        var beats = seconds / 60 * m_Bpm;
        var ticks = beats * MidiTokenizer.ticks_per_beat;
        return (long)ticks;
    }

    // async void StartGenerating()
    // {
    //     m_CancellationTokenSource = new CancellationTokenSource();
    //
    //     m_Generating = true;
    //     
    //     m_LatestGeneration = null;
    //     m_LatestGeneration = await m_Inference.Generate(m_CancellationTokenSource.Token);
    //     
    //     // var seq = m_Inference.results;
    //     // Play();
    //
    //     m_Generating = false;
    // }

    public async void GenerateAsync()
    {
        m_PlayQueues.Clear();
        CurrentGenerationLength = 0;
        
        m_CancellationTokenSource = new CancellationTokenSource();
        IsGenerating = true;

        var currentTicks = 0;
        await foreach(var tokenSequence in m_Inference.GenerateAsync(m_CancellationTokenSource.Token))
        {
            var (events, ticks) = m_Inference.tokenizer.detokenize(tokenSequence, currentTicks);
            QueueEvents(events);
            currentTicks = ticks;
            CurrentGenerationLength += tokenSequence.Length;
        }
        
        IsGenerating = false;
    }
}
