using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    public GameObject[] SrcTens;
    public GameObject[] SrcOnes;
    public GameObject[] DestTens;
    public GameObject[] DestOnes;

    private static string[] _segmentMap = new[] { "1111101", "1001000", "0111011", "1011011", "1001110", "1010111", "1110111", "1001001", "1111111", "1011111" };

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Polyhedron _polyhedron;
    private int _curFace;

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        SetRandomPolyhedron();
        SetCurFace(Rnd.Range(0, _polyhedron.Faces.Length));
        for (int i = 0; i < Arrows.Length; i++)
            Arrows[i].OnInteract = getArrowHandler(i);
    }

    private KMSelectable.OnInteractHandler getArrowHandler(int i)
    {
        return delegate
        {
            Arrows[i].AddInteractionPunch();
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Arrows[i].transform);

            if (_polyhedron.Faces[_curFace].AdjacentFaces[i] == null)
                Module.HandleStrike();
            else
                startRotation(_polyhedron.Faces[_curFace].AdjacentFaces[i].Value);
            return false;
        };
    }

    private void SetRandomPolyhedron()
    {
        SetPolyhedron(Data.Polyhedra[Rnd.Range(0, Data.Polyhedra.Length)].Name);
    }

    private void SetPolyhedron(string name)
    {
        _polyhedron = Data.Polyhedra.First(p => p.Name.Contains(name));
        Polyhedron.mesh = PolyhedronMeshes.First(inf => inf.name == _polyhedron.Name);
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
        }
        for (int i = _polyhedron.Faces[face].Vertices.Length; i < Arrows.Length; i++)
            Arrows[i].gameObject.SetActive(false);

        setDisplay(SrcTens, SrcOnes, face);
        Polyhedron.transform.localRotation = rotationTo(face);
    }

    private float easeOutSine(float time, float duration, float from, float to)
    {
        return (to - from) * Mathf.Sin(time / duration * (Mathf.PI / 2)) + from;
    }

    private IEnumerator rotate(int destFace)
    {
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
    }

    private void setDisplay(GameObject[] tens, GameObject[] ones, int number)
    {
        for (int i = 0; i < 7; i++)
        {
            tens[i].SetActive(_segmentMap[number / 10][i] == '1');
            ones[i].SetActive(_segmentMap[number % 10][i] == '1');
        }
    }

    private IEnumerator ProcessTwitchCommand(string command)
    {
        Match m;

        if (command == "reset")
        {
            SetRandomPolyhedron();
            SetCurFace(Rnd.Range(0, _polyhedron.Faces.Length));
        }
        else if ((m = Regex.Match(command, @"^set (.*) (\d+)$")).Success)
        {
            SetPolyhedron(m.Groups[1].Value);
            SetCurFace(int.Parse(m.Groups[2].Value));
        }
        return null;
    }
}
