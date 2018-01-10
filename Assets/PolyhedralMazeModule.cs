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

    public GameObject[] SrcTens;
    public GameObject[] SrcOnes;
    public GameObject[] DestTens;
    public GameObject[] DestOnes;

    private static string[] _segmentMap = new[] { "1111101", "1001000", "0111011", "1011011", "1001110", "1010111", "1110111", "1001001", "1111111", "1011111" };

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Polyhedron _polyhedron;
    private int _curFace;
    private int _startFace;
    private int _destFace;
    private bool _isSolved;
    private List<int> _route;
    private int[] _clockfaceToArrow = new int[12];
    private bool _coroutineActive = false;

    private const int _minSteps = 5;
    private const int _maxSteps = 7;

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

        SetPolyhedron(Data.Polyhedra[Rnd.Range(0, Data.Polyhedra.Length)].Name);
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
                if (_polyhedron.Faces[_curFace].AdjacentFaces[i] == null)
                {
                    if (_route.Count > 1)
                        Debug.LogFormat(@"[Polyhedral Maze #{0}] Route you took before strike: {1}", _moduleId, _route.JoinString(" → "));
                    Debug.LogFormat(@"[Polyhedral Maze #{0}] Walking from face #{1}, you ran into the wall marked “!”: [{2}] (clockwise order).", _moduleId, _curFace, _polyhedron.Faces[_curFace].AdjacentFaces.Select((adj, j) => adj == null ? j == i ? "WALL!" : "WALL" : adj.Value.ToString()).Reverse().JoinString(", "));
                    _route.Clear();
                    _route.Add(_curFace);
                    Module.HandleStrike();
                }
                else
                {
                    var face = _polyhedron.Faces[_curFace].AdjacentFaces[i].Value;
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
        _startFace = Enumerable.Range(0, _polyhedron.Faces.Length).Where(fIx => _polyhedron.Faces[fIx].AdjacentFaces.All(f => f != null)).PickRandom();

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
            for (int i = 0; i < _polyhedron.Faces[face].AdjacentFaces.Length; i++)
                if (_polyhedron.Faces[face].AdjacentFaces[i] != null)
                {
                    qFaces.Enqueue(_polyhedron.Faces[face].AdjacentFaces[i].Value);
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
        // return Quaternion.FromToRotation(_polyhedron.Faces[face].Normal, new Vector3(0, 1, 0));
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
            Quaternion rot2;
            if (arrowVector == -_polyhedron.Faces[face].Normal)
                rot2 = Quaternion.AngleAxis(180, rot1 * arrowDirection);
            else
                rot2 = Quaternion.FromToRotation(arrowVector, _polyhedron.Faces[face].Normal);

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

    private float easeOutSine(float time, float duration, float from, float to)
    {
        return (to - from) * Mathf.Sin(time / duration * (Mathf.PI / 2)) + from;
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
            Polyhedron.transform.localRotation = Quaternion.Slerp(srcRotation, destRotation, easeOutSine(time, duration, 0, 1));
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
    private string TwitchHelpMessage = @"Express your move as a sequence of numbers, e.g. “!{0} 1 2 3 4”. The first number in each command is a clockface direction (1–12) and selects the arrow closest to that direction. All subsequent numbers within the same command select an edge counting clockwise from the edge that was traversed last. For example, 1 is an immediate left-turn. In a five-sided face, 4 is an immediate right-turn. Use “!{0} reset” to reset the module.";
#pragma warning restore 414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        var pieces = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (pieces.Length == 1 && pieces[0] == "reset")
        {
            yield return null;
            ResetButton.OnInteract();
        }
        else if (pieces.Length >= 1 && pieces.All(p => { int val; return int.TryParse(p, out val) && val >= 1 && val <= 12; }))
        {
            yield return null;

            // First move: clockface
            var prevFace = _curFace;
            Arrows[_clockfaceToArrow[int.Parse(pieces[0]) % 12]].OnInteract();
            yield return null;

            while (_coroutineActive)
                yield return new WaitForSeconds(.2f);

            // From there on: clockwise from the one you came from
            for (int i = 1; i < pieces.Length; i++)
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
}
