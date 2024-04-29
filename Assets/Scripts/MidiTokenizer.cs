using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Pool;

public class MidiTokenizer
{
    public readonly int pad_id;
    public readonly int bos_id;
    public readonly int eos_id;
    public int vocab_size = 0;
    public readonly Dictionary<string, int> event_ids;
    public readonly Dictionary<int, string> id_events;
    public readonly Dictionary<string, int[]> parameter_ids;
    public readonly int max_token_seq;
    public const int ticks_per_beat = 480;

    public static readonly Dictionary<string, string[]> events = new()
    {
        { "note", new[] { "time1", "time2", "track", "duration", "channel", "pitch", "velocity" } },
        { "patch_change", new []{"time1", "time2", "track", "channel", "patch"} },
        { "control_change", new[] { "time1", "time2", "track", "channel", "controller", "value" } },
        { "set_tempo", new[] { "time1", "time2", "track", "bpm" } }
    };

    static readonly Dictionary<string, int> event_parameters = new()
    {
        {"time1", 128}, {"time2", 16}, {"duration", 2048}, {"track", 128}, {"channel", 16}, {"pitch", 128}, {"velocity", 128},
        {"patch", 128}, {"controller", 128}, {"value", 128}, {"bpm", 256}
    };

    public MidiTokenizer()
    {
        pad_id = allocate_ids(1)[0];
        bos_id = allocate_ids(1)[0];
        eos_id = allocate_ids(1)[0];
        
        event_ids = new();
        foreach (var eventsKey in events.Keys)
        {
            event_ids[eventsKey] = allocate_ids(1)[0];
        }
        
        id_events = new();
        foreach (var (key, value) in event_ids)
        {
            id_events[value] = key;
        }
        
        parameter_ids = new();
        foreach (var (key, value) in event_parameters)
        {
            parameter_ids[key] = allocate_ids(value);
        }

        max_token_seq = events.Values.Select(ps => ps.Length).Max() + 1;
    }

    public static float tempo2bpm(int tempo)
    {
        float tempof = (float)tempo / 1000000;
        return 60 / tempof;
    }
    
    static int bpm2tempo(float bpm)
    {
        if (bpm == 0)
        {
            bpm = 1;
        }
        
        return (int)(60 / bpm * 1000000);
    }

    int[] allocate_ids(int size)
    {
        List<int> ids = new();
        for (int i = vocab_size; i < vocab_size + size; i++)
        {
            ids.Add(i);
        }
        vocab_size += size;
        return ids.ToArray();
    }

    public struct Event
    {
        public string name;
        public List<int> parameters;
    }

    public List<List<Event>> detokenize(IEnumerable<IList<int>> mid_seq)
    {
        var tracks_dict = DictionaryPool<int, List<Event>>.Get();
        int t1 = 0;

        var parameters = ListPool<int>.Get();
        
        foreach (var tokens in mid_seq)
        {
            // first token element contains the event ID
            if (!id_events.ContainsKey(tokens[0])) continue;

            var name = id_events[tokens[0]];

            if (tokens.Count <= events[name].Length) continue;

            // Extract the remaining parameters from the token
            parameters.Clear();
            
            var valid = true;
            for (int i = 0; i < events[name].Length; i++)
            {
                var p = events[name][i];
                var parameter = tokens[i + 1] - parameter_ids[p][0];
                if (parameter < 0 || parameter >= event_parameters[p])
                {
                    valid = false;
                    break;
                }
                parameters.Add(parameter);
            }

            if (!valid) continue;

            // Need to make some timing adjustments for certain events
            if (name == "set_tempo")
            {
                parameters[3] = bpm2tempo(parameters[3]);
            }
            else if (name == "note")
            {
                parameters[3] = (int)(parameters[3] * ticks_per_beat / 16);
            }

            // Get timing info from params
            t1 += parameters[0];
            var t = t1 * 16 + parameters[1];
            t = (int)(t * ticks_per_beat / 16);

            // Get track idx
            var track_idx = parameters[2];
            if (!tracks_dict.ContainsKey(track_idx))
            {
                tracks_dict.Add(track_idx, new List<Event>());
            }

            // We have enough data to create the event structure
            var trackEvent = new Event()
            {
                name = name,
                parameters = new List<int>() { t } // FIXME avoid GC alloc
            };

            for (int i = 3; i < parameters.Count; i++)
            {
                trackEvent.parameters.Add(parameters[i]);
            }
            
            tracks_dict[track_idx].Add(trackEvent);
        }

        // FIXME: GC alloc
        var tracks = tracks_dict.Values.ToList();
        
        // To eliminate note overlap
        for (int i = 0; i < tracks.Count; i++)
        {
            Dictionary<(int, int), int> last_note_t = new ();
            List<Event> zero_len_notes = ListPool<Event>.Get();
            // sort by time reversed
            var track = tracks[i].OrderByDescending(e => e.parameters[0]);
            foreach (var ev in track)
            {
                if (ev.name == "note")
                {
                    var t = ev.parameters[0];
                    var d = ev.parameters[1];
                    var c = ev.parameters[2];
                    var p = ev.parameters[3];
                    var key = (c, p);
                    if (last_note_t.TryGetValue(key, out var value))
                    {
                        d = Math.Min(d, Math.Max(value - t, 0));
                    }

                    last_note_t[key] = t;
                    ev.parameters[1] = d;
                    if (d == 0)
                    {
                        zero_len_notes.Add(ev);
                    }
                }
            }

            foreach (var ev in zero_len_notes)
            {
                tracks[i].Remove(ev);
            }

            // tracks[i] = track;
            
            ListPool<Event>.Release(zero_len_notes);
        }

        
        ListPool<int>.Release(parameters);
        DictionaryPool<int, List<Event>>.Release(tracks_dict);

        return tracks;
    }
}
