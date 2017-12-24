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
    public TextMesh Label;
    public Transform Arrow;

    private List<GameObject> _createdLabels = new List<GameObject>();
    private List<GameObject> _createdArrows = new List<GameObject>();

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Polyhedron _polyhedron;
    private int _curFace;

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        SetRandomPolyhedron();
        SetCurFace(Rnd.Range(0, _polyhedron.Faces.Length));
    }

    private void SetRandomPolyhedron()
    {
        SetPolyhedron(Data.Polyhedra[Rnd.Range(0, Data.Polyhedra.Length)].Name);
    }

    private void SetPolyhedron(string name)
    {
        foreach (var label in _createdLabels)
            Destroy(label);
        _createdLabels.Clear();

        _polyhedron = Data.Polyhedra.First(p => p.Name.Contains(name));
        Polyhedron.mesh = PolyhedronMeshes.First(inf => inf.name == _polyhedron.Name);

        Debug.LogFormat("[Polyhedral Maze #{0}] Polyhedron: {1}", _moduleId, _polyhedron.ReadableName);

        for (int i = 0; i < _polyhedron.Faces.Length; i++)
        {
            var obj = Instantiate(Label);
            obj.transform.parent = Label.transform.parent;
            obj.text = i.ToString();
            var v = _polyhedron.Faces[i].Normal;
            var d = _polyhedron.Faces[i].Distance * 1.0001f;
            obj.transform.localPosition = new Vector3(d * v.x, d * v.y, d * v.z);
            obj.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
            obj.transform.localRotation = Quaternion.FromToRotation(new Vector3(0, 0, -1), v);
            obj.gameObject.name = string.Format("Label {0}", i);
            obj.gameObject.SetActive(true);
            _createdLabels.Add(obj.gameObject);
        }
        Label.gameObject.SetActive(false);
    }

    private void SetCurFace(int face)
    {
        Debug.LogFormat("[Polyhedral Maze #{0}] Settings face to: {1}", _moduleId, face);

        const float sizeFactor = .1f;

        foreach (var arrow in _createdArrows)
            Destroy(arrow);
        _createdArrows.Clear();
        _curFace = face;

        for (int i = 0; i < _polyhedron.Faces[face].Vertices.Length; i++)
        {
            var v1 = _polyhedron.Faces[face].Vertices[i];
            var v2 = _polyhedron.Faces[face].Vertices[(i + 1) % _polyhedron.Faces[face].Vertices.Length];
            var obj = Instantiate(Arrow);
            obj.transform.parent = Arrow.parent;
            obj.transform.localScale = new Vector3(sizeFactor, sizeFactor, sizeFactor);
            obj.transform.localPosition = new Vector3((v1.x + v2.x) / 2, (v1.y + v2.y) / 2, (v1.z + v2.z) / 2);

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

            obj.transform.localRotation = rot2 * Quaternion.FromToRotation(arrowDirection, Vector3.Cross(_polyhedron.Faces[face].Normal, v2 - v1));
            obj.gameObject.SetActive(true);
            _createdArrows.Add(obj.gameObject);
        }

        Polyhedron.transform.localRotation = Quaternion.FromToRotation(_polyhedron.Faces[face].Normal, new Vector3(0, 1, 0));
        Arrow.gameObject.SetActive(false);
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
