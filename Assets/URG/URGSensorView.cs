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
    public int _th;
    public int _minSize;
    public int _maxSize;
    public GameObject _marker;
    private GameObject _spawn;

    void Start()
    {
        _urg = new URGSensor();
        _urg.OpenStream("192.168.0.10", 0, 2160);
        _spawn = null;
    }

    void Update()
    {
        // set object detection param
        _urg.SetDetectParam(_th, _minSize, _maxSize);

        // get urg pose matrix
        var q = transform.worldToLocalMatrix.rotation;
        var m = Matrix4x4.identity;
        m.SetTRS(transform.position, q, transform.localScale);
       _urg.Pose = m;

        var distance = _urg.Distances;
        var org = transform.position;

        var count = 0;
        for (int i = 0; i < distance.Length - 1; i++)
        {
            var p = _urg.CalcPos(i);
            Debug.DrawLine(org, p, new Color(0.2f, 0.2f, 0.2f), 0, false);

            var dd = distance[i] - distance[i + 1];
            if (dd > _th)
            {
                if (count == 0)
                {
                    Debug.DrawLine(org, p, new Color(1.0f, 0.0f, 0.0f), 0, false);
                    ++count;
                }
            }
            else if (dd < -_th)
            {
                Debug.DrawLine(org, p, new Color(0.0f, 1.0f, 0.0f), 0, false);
                count = 0;
            }
            else
            {
                if (count > 0)
                {
                    ++count;
                }
            }
        }

        var objs = _urg.Objs;
        if (objs == null) return;
        //for (int i = 0; i < objs.Length; i++)
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