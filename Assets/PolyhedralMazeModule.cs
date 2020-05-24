using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PolyhedralMaze;
using UnityEngine;
using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Polyhedral Maze
/// Created by Timwi
/// </summary>
public class PolyhedralMazeModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;
    public Mesh[] PolyhedronMeshes;
    public MeshFilter Polyhedron;
    public KMSelectable[] Arrows;
    public KMSelectable ResetButton;
    public KMRuleSeedable RuleSeedable;

    public GameObject[] SrcTens;
    public GameObject[] SrcOnes;
    public GameObject[] DestTens;
    public GameObject[] DestOnes;

    private static readonly string[] _segmentMap = new[] { "1111101", "1001000", "0111011", "1011011", "1001110", "1010111", "1110111", "1001001", "1111111", "1011111" };

    // Rules
    // Keys are: (1) rule seed, (2) polyhedron name, (3+4) pairs of faces
    private static readonly Dictionary<int, Dictionary<string, Dictionary<int, HashSet<int>>>> _allPermissibleTransitions = new Dictionary<int, Dictionary<string, Dictionary<int, HashSet<int>>>>();
    // Keys are: (1) rule seed, (2) list of possible starting faces
    private static readonly Dictionary<int, int[]> _allStartingPositions = new Dictionary<int, int[]>();
    public static int[][] _allPossibleStartingPositions = new int[][]
    {
        new[] { 0, 6, 9, 13, 17, 29 },
        new[] { 0, 6, 13, 29, 31, 35 },
        new[] { 0, 6, 29, 31, 35, 37 },
        new[] { 0, 13, 15, 21, 31, 35 },
        new[] { 0, 13, 15, 29, 31, 35 },
        new[] { 1, 3, 7, 9, 12, 18 },
        new[] { 2, 5, 11, 16, 20, 22 },
        new[] { 2, 5, 11, 16, 22, 32 },
        new[] { 2, 5, 11, 17, 20, 22 },
        new[] { 2, 5, 11, 17, 22, 32 },
        new[] { 3, 7, 9, 12, 18, 28 },
        new[] { 19, 21, 24, 28, 35, 38 },
        new[] { 19, 21, 27, 31, 35, 38 }
    };

    // Transitions for the current rule seed and current polyhedron
    private Dictionary<int, HashSet<int>> _permissibleTransitions;
    // Starting positions for the current rule seed
    private int[] _startingPositions;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Polyhedron _polyhedron;
    private int _curFace;
    private int _startFace;
    private int _destFace;
    private bool _isSolved;
    private List<int> _route;
    private readonly int[] _clockfaceToArrow = new int[12];
    private bool _coroutineActive = false;

    private const int _minSteps = 5;
    private const int _maxSteps = 11;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        _isSolved = false;
        _route = new List<int>();

        for (int i = 0; i < Arrows.Length; i++)
            Arrows[i].OnInteract = getArrowHandler(i);

        ResetButton.OnInteract = delegate
        {
            ResetButton.AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, ResetButton.transform);
            if (_isSolved)
                return false;

            if (_route.Count > 1)
                Debug.LogFormat(@"[Polyhedral Maze #{0}] Route you took before reset: {1}", _moduleId, _route.JoinString(" → "));
            _route.Clear();
            _route.Add(_startFace);
            startRotation(_startFace);
            return false;
        };

        Bomb.OnBombExploded = delegate
        {
            if (_route.Count > 1)
                Debug.LogFormat(@"[Polyhedral Maze #{0}] Route you took before explosion: {1}", _moduleId, _route.JoinString(" → "));
            _route.Clear();
        };

        // RULE SEED
        var rnd = RuleSeedable.GetRNG();
        if (!_allPermissibleTransitions.ContainsKey(rnd.Seed))
        {
            var starts = _allPossibleStartingPositions[rnd.Next(0, _allPossibleStartingPositions.Length)];
            _allStartingPositions[rnd.Seed] = starts;
            var allTransitions = new Dictionary<string, Dictionary<int, HashSet<int>>>();    // all permissible transitions for this rule seed

            foreach (var polyhedron in Data.Polyhedra)
            {
                var transitions = new Dictionary<int, HashSet<int>>();
                var allowTransition = new Action<int, int>((face1, face2) =>
                {
                    HashSet<int> h;
                    if (!transitions.TryGetValue(face1, out h))
                        transitions[face1] = h = new HashSet<int>();
                    h.Add(face2);
                    if (!transitions.TryGetValue(face2, out h))
                        transitions[face2] = h = new HashSet<int>();
                    h.Add(face1);
                });

                // Time for a slightly more complex maze generation algorithm!
                // We want to generate a maze in which specific faces (0, 6, 13, 29, 31, 35) have no walls around them.
                // Here is how this algorithm proceeds:
                //  1) We have 6 “active groups”, one for each starting face. Each active group starts out containing a starting face and its neighbors, and the walls between those are removed right at the start.
                //  2) Choose a random active group and a random face within it.
                //  3) From that face’s edges, choose a random edge that goes to a face that isn’t in any group yet.
                //  4) If there is no such valid edge, move that face to ‘done’. (Go to 2.)
                //  5) Remove the wall on this edge (i.e., make the two faces connected).
                //  6) Add the new face to the current group.
                //  7) Keep doing this (steps 2–6) until the groups cover the entire polyhedron.
                //  8) Now, find a random connection between every pair of groups and remove walls to connect all the groups.
                //      (Note this creates loops. This is intentional. We don’t want a super tight maze.)
                Debug.LogFormat(@"<Polyhedral Maze #{0}> Generating maze for {1} = {2} ({3} faces)...", _moduleId, polyhedron.Name, polyhedron.ReadableName, polyhedron.Faces.Length);

                // Step 1
                var done = starts.Select(f => new List<int>()).ToArray();
                var active = starts.Select(f => new List<int> { f }).ToArray();
                var facesCovered = starts.Length;
                for (var gr = 0; gr < starts.Length; gr++)
                    foreach (var adj in polyhedron.Faces[starts[gr]].AdjacentFaces)
                    {
                        if (!active.Any(a => a.Contains(adj)))
                        {
                            active[gr].Add(adj);
                            facesCovered++;
                        }
                        allowTransition(starts[gr], adj);
                    }

                // Steps 2–7
                while (facesCovered < polyhedron.Faces.Length)
                {
                    var gr = rnd.Next(0, active.Length);
                    if (active[gr].Count == 0)
                        continue;
                    var rndFaceIx = rnd.Next(0, active[gr].Count);
                    var rndFace = active[gr][rndFaceIx];
                    var validAdjs = polyhedron.Faces[rndFace].AdjacentFaces.Where(f => !active.Any(a => a.Contains(f)) && !done.Any(d => d.Contains(f))).ToArray();
                    if (validAdjs.Length == 0)
                    {
                        done[gr].Add(rndFace);
                        active[gr].RemoveAt(rndFaceIx);
                        continue;
                    }
                    var adjIx = rnd.Next(0, validAdjs.Length);
                    var adj = validAdjs[adjIx];
                    active[gr].Add(adj);
                    allowTransition(rndFace, adj);
                    facesCovered++;
                }

                // Step 8
                for (var i = 0; i < done.Length; i++)
                {
                    done[i].AddRange(active[i]);
                    rnd.ShuffleFisherYates(done[i]);
                }
                for (var d1 = 0; d1 < done.Length; d1++)
                    for (var d2 = d1 + 1; d2 < done.Length; d2++)
                    {
                        var found = false;
                        for (var f1 = 0; !found && f1 < done[d1].Count; f1++)
                            for (var e1 = 0; !found && e1 < polyhedron.Faces[done[d1][f1]].AdjacentFaces.Length; e1++)
                                if (done[d2].Contains(polyhedron.Faces[done[d1][f1]].AdjacentFaces[e1]))
                                {
                                    allowTransition(done[d1][f1], polyhedron.Faces[done[d1][f1]].AdjacentFaces[e1]);
                                    found = true;
                                }
                    }

                allTransitions[polyhedron.Name] = transitions;
            }
            _allPermissibleTransitions[rnd.Seed] = allTransitions;
        }
        var curPolyhedron = Data.Polyhedra[Rnd.Range(0, Data.Polyhedra.Length)].Name;
        _permissibleTransitions = _allPermissibleTransitions[rnd.Seed][curPolyhedron];
        _startingPositions = _allStartingPositions[rnd.Seed];
        SetPolyhedron(curPolyhedron);

        Polyhedron.GetComponent<MeshRenderer>().materials[1].color = Color.HSVToRGB(Rnd.Range(0f, 1f), Rnd.Range(.6f, .9f), Rnd.Range(.3f, .7f));
        _route.Add(_curFace);
    }

    private KMSelectable.OnInteractHandler getArrowHandler(int i)
    {
        return delegate
        {
            Arrows[i].AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Arrows[i].transform);

            if (!_isSolved)
            {
                HashSet<int> p;
                if (!_permissibleTransitions.TryGetValue(_curFace, out p) || !p.Contains(_polyhedron.Faces[_curFace].AdjacentFaces[i]))
                {
                    if (_route.Count > 1)
                        Debug.LogFormat(@"[Polyhedral Maze #{0}] Route you took before strike: {1}", _moduleId, _route.JoinString(" → "));
                    Debug.LogFormat(@"[Polyhedral Maze #{0}] You tried to go from face #{1} to face #{2}, but there’s a wall there.", _moduleId, _curFace, _polyhedron.Faces[_curFace].AdjacentFaces[i]);
                    _route.Clear();
                    _route.Add(_curFace);
                    Module.HandleStrike();
                }
                else
                {
                    var face = _polyhedron.Faces[_curFace].AdjacentFaces[i];
                    _route.Add(face);
                    startRotation(face);
                }
            }

            return false;
        };
    }

    private void SetPolyhedron(string name)
    {
        _polyhedron = Data.Polyhedra.First(p => p.Name.Contains(name));
        Polyhedron.mesh = PolyhedronMeshes.First(inf => inf.name == _polyhedron.Name);

        Debug.LogFormat(@"[Polyhedral Maze #{0}] Your polyhedron is a {1}.", _moduleId, _polyhedron.ReadableName);

        // Find a suitable start face
        _startFace = _startingPositions.PickRandom();

        Debug.LogFormat(@"[Polyhedral Maze #{0}] You are starting on face #{1}.", _moduleId, _startFace);

        // Run a breadth-first search to determine a destination face that is the desired number of steps away
        var qFaces = new Queue<int>(); qFaces.Enqueue(_startFace);
        var qDistances = new Queue<int>(); qDistances.Enqueue(0);
        var already = new HashSet<int>();
        var suitableDestinationFaces = new List<int>();

        while (qFaces.Count > 0)
        {
            var face = qFaces.Dequeue();
            var distance = qDistances.Dequeue();
            if (!already.Add(face))
                continue;
            if (distance >= _minSteps && distance <= _maxSteps)
                suitableDestinationFaces.Add(face);
            var permissible = _permissibleTransitions[face];
            foreach (var adj in _polyhedron.Faces[face].AdjacentFaces)
                if (permissible.Contains(adj))
                {
                    qFaces.Enqueue(adj);
                    qDistances.Enqueue(distance + 1);
                }
        }

        _destFace = suitableDestinationFaces.PickRandom();
        setDisplay(DestTens, DestOnes, _destFace);
        SetCurFace(_startFace);

        Debug.LogFormat(@"[Polyhedral Maze #{0}] You need to go to face #{1}.", _moduleId, _destFace);
    }

    private void startRotation(int face)
    {
        for (int i = 0; i < Arrows.Length; i++)
            Arrows[i].gameObject.SetActive(false);
        StartCoroutine(rotate(face));
    }

    private Quaternion rotationTo(int face)
    {
        var normal = _polyhedron.Faces[face].Normal;
        var curNormal = Polyhedron.transform.localRotation * normal;
        var newRotation = Quaternion.FromToRotation(curNormal, new Vector3(0, 1, 0)) * Polyhedron.transform.localRotation;
        return newRotation;
    }

    private void SetCurFace(int face)
    {
        const float sizeFactor = .1f;

        _curFace = face;

        setDisplay(SrcTens, SrcOnes, face);
        Polyhedron.transform.localRotation = rotationTo(face);

        if (face == _destFace)
        {
            if (_route.Count > 1)
                Debug.LogFormat(@"[Polyhedral Maze #{0}] Route you took before solve: {1}", _moduleId, _route.JoinString(" → "));
            Debug.LogFormat(@"[Polyhedral Maze #{0}] Module solved.", _moduleId);
            Module.HandlePass();
            _isSolved = true;
            for (int i = 0; i < Arrows.Length; i++)
                Arrows[i].gameObject.SetActive(false);
            return;
        }

        // Clockface directions
        double[] angleDiffs = new double[12];
        for (int i = 0; i < 12; i++)
            _clockfaceToArrow[i] = -1;

        for (int i = 0; i < _polyhedron.Faces[face].Vertices.Length; i++)
        {
            var v1 = _polyhedron.Faces[face].Vertices[i];
            var v2 = _polyhedron.Faces[face].Vertices[(i + 1) % _polyhedron.Faces[face].Vertices.Length];
            Arrows[i].transform.localScale = new Vector3(sizeFactor, sizeFactor, sizeFactor);
            Arrows[i].transform.localPosition = new Vector3((v1.x + v2.x) / 2, (v1.y + v2.y) / 2, (v1.z + v2.z) / 2);

            // y = arrow face normal
            var arrowFaceNormal = Vector3.up;
            // z = arrow direction
            var arrowDirection = Vector3.forward;

            var vectorPointingAwayFromFace = Vector3.Cross(_polyhedron.Faces[face].Normal, v2 - v1);
            var rot1 = Quaternion.FromToRotation(arrowDirection, vectorPointingAwayFromFace);
            var arrowVector = rot1 * arrowFaceNormal;

            // Special case! If the arrow is exactly upside-down, the rotation is a degenerate case.
            var rot2 = arrowVector == -_polyhedron.Faces[face].Normal
                ? Quaternion.AngleAxis(180, rot1 * arrowDirection)
                : Quaternion.FromToRotation(arrowVector, _polyhedron.Faces[face].Normal);
            Arrows[i].transform.localRotation = rot2 * Quaternion.FromToRotation(arrowDirection, Vector3.Cross(_polyhedron.Faces[face].Normal, v2 - v1));
            Arrows[i].gameObject.SetActive(true);

            var arrowAngle = (Polyhedron.transform.localRotation * Arrows[i].transform.localRotation).eulerAngles.y;
            for (int c = 0; c < 12; c++)
            {
                var cAngle = c * 30;
                var diff = Math.Min(Math.Min(Math.Abs(arrowAngle - cAngle), Math.Abs(arrowAngle - cAngle - 360)), Math.Abs(arrowAngle - cAngle + 360));
                if (_clockfaceToArrow[c] == -1 || diff < angleDiffs[c])
                {
                    _clockfaceToArrow[c] = i;
                    angleDiffs[c] = diff;
                }
            }
        }
        for (int i = _polyhedron.Faces[face].Vertices.Length; i < Arrows.Length; i++)
            Arrows[i].gameObject.SetActive(false);
    }

    private IEnumerator rotate(int destFace)
    {
        _coroutineActive = true;

        const float duration = .5f;
        yield return null;

        var srcRotation = Polyhedron.transform.localRotation;
        var destRotation = rotationTo(destFace);

        float time = 0;
        while (time < duration)
        {
            time += Time.deltaTime;
            Polyhedron.transform.localRotation = Quaternion.Slerp(srcRotation, destRotation, Easing.OutSine(time, 0, 1, duration));
            yield return null;
        }
        SetCurFace(destFace);
        _coroutineActive = false;
    }

    private void setDisplay(GameObject[] tens, GameObject[] ones, int number)
    {
        for (int i = 0; i < 7; i++)
        {
            tens[i].SetActive(_segmentMap[(number / 10) % 10][i] == '1');
            ones[i].SetActive(_segmentMap[number % 10][i] == '1');
        }
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Express your move as a sequence of numbers, e.g. “!{0} move 1 2 3 4”. The first number in each command is a clockface direction (1–12) and selects the arrow closest to that direction. All subsequent numbers within the same command select an edge counting clockwise from the edge that was traversed last. For example, 1 is an immediate left-turn. In a five-sided face, 4 is an immediate right-turn. Use “!{0} reset” to reset the module.";
#pragma warning restore 414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        var pieces = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var skip = 0;
        if (pieces.Length > 1 && pieces[0].Equals("move", StringComparison.InvariantCultureIgnoreCase))
            skip = 1;

        if (pieces.Length == 1 && pieces[0].Equals("reset", StringComparison.InvariantCultureIgnoreCase))
        {
            yield return null;
            ResetButton.OnInteract();
        }
        else if (pieces.Length > skip && pieces.Skip(skip).All(p => { int val; return int.TryParse(p, out val) && val >= 1 && val <= 12; }))
        {
            yield return null;

            // First move: clockface
            var prevFace = _curFace;
            Arrows[_clockfaceToArrow[int.Parse(pieces[skip]) % 12]].OnInteract();
            yield return null;

            while (_coroutineActive)
                yield return new WaitForSeconds(.2f);

            // From there on: clockwise from the one you came from
            for (int i = skip + 1; i < pieces.Length; i++)
            {
                var direction = int.Parse(pieces[i]);
                var fromIndex = Array.IndexOf(_polyhedron.Faces[_curFace].AdjacentFaces, prevFace);
                if (direction < 1 || direction >= _polyhedron.Faces[_curFace].AdjacentFaces.Length)
                {
                    yield return string.Format("sendtochat The number {0} (#{1} in your input) was not a valid direction. Aborting there.", direction, i);
                    yield break;
                }
                prevFace = _curFace;
                Arrows[(fromIndex + _polyhedron.Faces[_curFace].AdjacentFaces.Length - direction) % _polyhedron.Faces[_curFace].AdjacentFaces.Length].OnInteract();
                yield return null;

                while (_coroutineActive)
                    yield return new WaitForSeconds(.2f);
            }
        }
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        // Breadth-first search
        var already = new HashSet<int>();
        var parents = new Dictionary<int, int>();
        var q = new Queue<int>();
        q.Enqueue(_curFace);

        while (q.Count > 0)
        {
            var f = q.Dequeue();
            if (!already.Add(f))
                continue;
            if (f == _destFace)
                goto found;

            var permissible = _permissibleTransitions[f];
            foreach (var neighbour in _polyhedron.Faces[f].AdjacentFaces)
            {
                if (!permissible.Contains(neighbour))   // That’s a wall
                    continue;
                if (already.Contains(neighbour))
                    continue;
                q.Enqueue(neighbour);
                parents[neighbour] = f;
            }
        }

        throw new Exception("There is a bug in this module’s auto-solve handler. Please contact Timwi about this.");

        found:
        var path = new List<int>();
        var face = _destFace;
        while (face != _curFace)
        {
            path.Add(Array.IndexOf(_polyhedron.Faces[parents[face]].AdjacentFaces, face));
            face = parents[face];
        }

        for (var i = path.Count - 1; i >= 0; i--)
        {
            Arrows[path[i]].OnInteract();
            yield return new WaitForSeconds(.1f);
            while (_coroutineActive)
                yield return true;
        }
    }
}
