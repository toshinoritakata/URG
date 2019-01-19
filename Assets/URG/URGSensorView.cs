using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System.Net.Sockets;
using SCIP_library;

public class URGSensorView : MonoBehaviour
{
    private URGSensor _urg;
    [SerializeField] private int _th;
    [SerializeField] private int _minSize;
    [SerializeField] private int _maxSize;

    public GameObject _marker;
    private GameObject _spawn;

    private Mesh _mesh;
    private List<Vector3> _vertices;
    private List<int> _triangles;
    [SerializeField]
    private Material _mat;

    void Start()
    {
        _urg = new URGSensor();
        _urg.OpenStream("192.168.0.10", 0, 2160);
        _spawn = null;

        var meshFilter = gameObject.AddComponent<MeshFilter>();
        var meshRenderer = gameObject.AddComponent<MeshRenderer>();
        _mesh = meshFilter.mesh;
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        meshRenderer.sharedMaterial = _mat;

        _mesh.Clear();

        var step = _urg.Steps;
        _vertices = new List<Vector3>();
        for (int i = 0; i < step; i++)
        {
            _vertices.Add(Vector3.zero);
        }
        _vertices.Add(Vector3.zero);
        _mesh.SetVertices(_vertices);

        _triangles = new List<int>();
        for (int i = 0; i < step - 1; i++)
        {
            _triangles.Add(_vertices.Count - 1);
            _triangles.Add(i + 1);
            _triangles.Add(i);
        }
        _mesh.SetTriangles(_triangles, 0);
    }

    void Update()
    {
        // set object detection param
        _urg.SetDetectParam(_th, _minSize, _maxSize);

        // get urg pose matrix
        _urg.Pose = transform.localToWorldMatrix;

        var distance = _urg.Distances;
        var org = transform.position;

        for (int i = 0; i < distance.Length; i++)
        {
            _vertices[i] = _urg.CalcRawPos(i);
        }
        _mesh.SetVertices(_vertices);
        _mesh.RecalculateBounds();
        _mesh.RecalculateNormals();

        var objs = _urg.Objs;
        if (objs != null)
        {
            var item = objs[0];
            if (_spawn == null)
            {
                var pp = new Vector3(item.x, item.y, item.z);
                StartCoroutine(bomb(pp));
            }
        }
    }

    IEnumerator bomb(Vector3 p)
    {
        _spawn = GameObject.Instantiate(_marker, p, Quaternion.identity);
        yield return new WaitForSeconds(1);
        GameObject.Destroy(_spawn);
        _spawn = null;
    }

    private void OnDestroy()
    {
        _urg.CloseStream();
    }
}