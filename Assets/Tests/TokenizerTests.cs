using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TokenizerTests
{
    static readonly List<List<int>> TestMidSequence = new()
    {
        new() { 1, 0, 0, 0, 0, 0, 0, 0 }, 
        new() { 4, 7, 135, 2199, 2327, 2599, 0, 0 },
        new() { 6, 7, 135, 2199, 3072, 0, 0, 0 }, 
        new() { 3, 7, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 167, 2327, 2410, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2418, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 167, 2327, 2413, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2415, 2543 },
        new() { 3, 8, 135, 2199, 167, 2327, 2415, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2415, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2415, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2418, 2543 },
        new() { 3, 8, 135, 2199, 167, 2327, 2418, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 167, 2327, 2410, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2410, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 167, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2417, 2543 },
        new() { 3, 8, 135, 2199, 159, 2327, 2410, 2543 },
        new() { 3, 7, 143, 2199, 159, 2327, 2415, 2543 }
    };
    
    // A Test behaves as an ordinary method
    [Test]
    public void TokenizerTestsSimplePasses()
    {
        // Use the Assert class to test conditions
        var tokenizer = new MidiTokenizer();
        Assert.AreEqual(0, tokenizer.pad_id);

        var tracks = tokenizer.detokenize(TestMidSequence);
        
        Assert.AreEqual(0, tokenizer.pad_id);
    }

    // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
    // `yield return null;` to skip a frame.
    [UnityTest]
    public IEnumerator TokenizerTestsWithEnumeratorPasses()
    {
        // Use the Assert class to test conditions.
        // Use yield to skip a frame.
        yield return null;
    }
}
