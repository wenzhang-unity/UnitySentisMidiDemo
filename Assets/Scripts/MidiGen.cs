using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MidiPlayerTK;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Inference2))]
[RequireComponent(typeof(AudioSource))]
public class MidiGen : MonoBehaviour
{
    Inference2 m_Inference;

    [SerializeField]
    GameObject m_Panel;
    
    [SerializeField]
    Slider m_Slider;

    [SerializeField]
    Button m_GenerateButton;

    [SerializeField]
    Button m_PlayButton;
    
    [SerializeField]
    MidiStreamPlayer m_midiPlayer;

    [SerializeField]
    OrbitCamera m_Camera;
    
    readonly List<Queue<MPTKEvent>> m_PlayQueues = new();

    TextMeshProUGUI m_GenerateButtonText;
    // TextMeshProUGUI m_PlayButtonText;

    float m_Bpm = 120;
    double m_StartTick;
    bool m_Generating;
    bool m_Playing;

    int m_CurrentGenerationLength;

    readonly ConcurrentQueue<MPTKEvent> m_PlayedNotes = new();

    public static event Action<MPTKEvent> OnNotePlayed;

    CancellationTokenSource m_CancellationTokenSource;

    int[][] m_LatestGeneration;
    bool CanPlay => m_LatestGeneration != null;

    // Start is called before the first frame update
    void Start()
    {
        m_Inference = GetComponent<Inference2>();
        m_GenerateButtonText = m_GenerateButton.GetComponentInChildren<TextMeshProUGUI>();
        
        m_GenerateButton.onClick.AddListener(OnGenerateButtonPressed);
        m_PlayButton.onClick.AddListener(OnPlayButtonPressed);
        
        m_Inference.onTokenGenerated += (length) =>
        {
            m_CurrentGenerationLength = length;
        };
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!m_Playing) return;
        
        bool hasEvents = false;
        var currentTicks = SecondsToTicks(AudioSettings.dspTime - m_StartTick);
        foreach (var queue in m_PlayQueues)
        {
            if (queue.Count == 0) continue;

            hasEvents = true;
            while (queue.Count > 0 && queue.Peek().Tick < currentTicks)
            {
                var midiEvent = queue.Dequeue();
                m_midiPlayer.MPTK_PlayEvent(midiEvent);
                if (midiEvent.Command is MPTKCommand.NoteOn)
                {
                    m_PlayedNotes.Enqueue(midiEvent);
                }
            }
        }

        if (!hasEvents)
        {
            Debug.Log("Done playing");
            m_Playing = false;
        }
    }

    void Update()
    {
        while (m_PlayedNotes.TryDequeue(out var midiEvent))
        {
            OnNotePlayed?.Invoke(midiEvent);
        }

        m_GenerateButtonText.text = m_Generating ? "Cancel" : "Generate";
        m_PlayButton.interactable = CanPlay;

        if (m_Generating)
        {
            m_Slider.value = (float)m_CurrentGenerationLength / m_Inference.max_len;
        }

        m_Panel.SetActive(!m_Playing);
        m_Camera.enabled = m_Playing;
    }

    void OnGenerateButtonPressed()
    {
        if (m_Generating)
        {
            m_CancellationTokenSource.Cancel();
        }
        else
        {
            StartGenerating();
        }
    }

    void OnPlayButtonPressed()
    {
        Play();
    }

    void Dummy()
    {
        if (CanPlay)
        {
            if (GUI.Button(new Rect(10, 10, 100, 50), "Play"))
            {
                Play();
            }
        }
        else if (m_Generating)
        {
            if (GUI.Button(new Rect(10, 10, 100, 50), "Cancel"))
            {
                m_CancellationTokenSource.Cancel();
            }
        }
        else
        {
            if (GUI.Button(new Rect(10, 10, 100, 50), "Generate"))
            {
                StartGenerating();
            }
        }
    }

    void Play()
    {
        if (m_Playing) return;
        
        QueueEvents(m_Inference.tokenizer.detokenize(m_LatestGeneration));
        
        m_Playing = true;
        m_StartTick = AudioSettings.dspTime;
        Debug.Log("Start playing");
        // StartCoroutine(PlayEvents());
    }

    void QueueEvents(List<List<MidiTokenizer.Event>> events)
    {
        for (int i = 0; i < events.Count; i++)
        {
            while (m_PlayQueues.Count <= i)
            {
                m_PlayQueues.Add(new Queue<MPTKEvent>());
            }

            var track = events[i];
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

                    Debug.Log($"note: {midiEvent.Channel}, {midiEvent.Value}");
                    
                    m_PlayQueues[i].Enqueue(midiEvent);
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
                m_PlayQueues[i].Enqueue(midiEvent);
            }
        }
    }

    long TicksToMilliseconds(int ticks)
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

    async void StartGenerating()
    {
        m_CancellationTokenSource = new CancellationTokenSource();

        m_Generating = true;
        
        m_LatestGeneration = null;
        m_LatestGeneration = await m_Inference.Generate(m_CancellationTokenSource.Token);
        
        // var seq = m_Inference.results;
        // Play();

        m_Generating = false;
    }
}
