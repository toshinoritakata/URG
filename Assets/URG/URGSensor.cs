using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using SCIP_library;
using System.Threading;
using UnityEngine;

public class URGSensor
{
    private TcpClient _urg;
    private NetworkStream _stream;
    private int _start_step = 0;
    private int _end_step = 2160;

    private List<long> _read_data;
    private Thread _thread;
    private long[] _distance;
    private long[] _calib_distance;
    private long[] _filtered_distance;
    private bool _isRun;
    private object _lockobj;
    private int _ares;
    private int _amax;
    private int _distanceGap = 100;
    private int _minSize = 20;
    private int _maxSize = 150;
    private Matrix4x4 _pose;
    public Matrix4x4 Pose { set { _pose = value; } }

    private List<Vector4> _objs;
    public Vector4[] Objs
    {
        get
        {
            lock (_lockobj)
            {
                if (_objs.Count == 0) return null;
                var arry = new Vector4[_objs.Count];
                _objs.CopyTo(arry);
                return arry;
            }
        }
    }

    public int Steps
    {
        get { return _amax + 1; }
    }

    public long[] Distances
    {
        get { return _distance; }
    }

    private void readSpec(string spec)
    {
        foreach (var sp in spec.Split('\n'))
        {
            var tokens = sp.Split(new char[] { ':', ';' });
            if (tokens[0] == "ARES") { _ares = int.Parse(tokens[1]); }
            if (tokens[0] == "AMAX") { _amax = int.Parse(tokens[1]); }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="gap"></param>
    /// <param name="minSize"></param>
    /// <param name="maxSize"></param>
    public void SetDetectParam(int gap, int minSize, int maxSize)
    {
        if (gap > 0) _distanceGap = gap;
        if (minSize > 0) _minSize = minSize;
        if (maxSize > _minSize) _maxSize = maxSize;
    }

    public void OpenStream(string ip_address, int start_step, int end_step)
    {
        const int port_number = 10940;

        _start_step = start_step;
        _end_step = end_step;

        _urg = new TcpClient();
        _urg.SendBufferSize = 0;
        _urg.ReceiveBufferSize = 0;
        _urg.Connect(ip_address, port_number);
        _stream = _urg.GetStream();

        write(_stream, SCIP_Writer.PP());
        readSpec(read_line(_stream));

        write(_stream, SCIP_Writer.SCIP2());
        read_line(_stream); // ignore echo back
        write(_stream, SCIP_Writer.MD(_start_step, _end_step));
        read_line(_stream);  // ignore echo back

        _lockobj = new object();
        _read_data = new List<long>();
        _distance = new long[_amax+1];
        _calib_distance = new long[_amax+1]; // store calibration data
        _filtered_distance = new long[_amax+1];

        _objs = new List<Vector4>();

        _isRun = true;
        _thread = new Thread(new ThreadStart(getDataWork));
        _thread.Start();
    }

    public void StoreCalibrationData()
    {
        _distance.CopyTo(_calib_distance, 0);
    }

    private Vector3 distToPos(int step, long dist)
    {
        var th = (Mathf.PI * 2f / _ares) * step - (Mathf.PI * 0.25f);
        var x = Mathf.Cos(th) * dist;
        var y = Mathf.Sin(th) * dist;
        return new Vector3(x, y, 0) * 0.001f; // mm -> m
    }

    /// <summary>
    /// get raw position  
    /// </summary>
    /// <param name="step"></param>
    /// <returns></returns>
    public Vector3 CalcRawPos(int step)
    {
        return distToPos(step, _distance[step]);
    }

    public Vector3 CalcCalibPos(int step)
    {
        return distToPos(step, Math.Abs(_distance[step] - _calib_distance[step]));
    }


    /// <summary>
    /// get position with pose 
    /// </summary>
    /// <param name="step"></param>
    /// <returns></returns>
    public Vector3 CalcPos(int step)
    {
        return _pose.MultiplyPoint(CalcRawPos(step));
    }

    public void GetObjs()
    {
        // median filer
        int filterSize = 3;
        var tmp = new long[filterSize];
        int mid = filterSize / 2;
        for (int i = 0; i < this.Steps; i++)
        {
            for (int n = 0; n < filterSize; n++)
            {
                var m = Math.Min(Math.Max(0, i + n - mid), this.Steps - 1);
                tmp[n] = Math.Abs(_distance[m] - _calib_distance[m]);
            }
            Array.Sort(tmp);
            _filtered_distance[i] = tmp[mid];
        }

        /*
        int filterSize = 3;
        long tmp = 0;
        int mid = filterSize/2;
        for (int i = 0; i < this.Steps; i++)
        {
            // box filter
            for (int n = 0; n < filterSize; n++)
            {
                var m = Math.Min(Math.Max(0, i + n - mid), this.Steps - 1);
                tmp += Math.Abs(_distance[m] - _calib_distance[m]);
            }
            _filtered_distance[i] = tmp / filterSize;

            // no filter
            //tmp = Math.Abs(_distance[i] - _calib_distance[i]);
            //_filtered_distance[i] = tmp;
        }

        // no filter
        for (int i = 0; i < this.Steps; i++)
        {
            _filtered_distance[i] = Math.Abs(_distance[i] - _calib_distance[i]);
        }
        */

        int count = 0;
        Vector3 sp = Vector3.zero;
        Vector3 cp = Vector3.zero;

        _objs.Clear();
        for (int i = 0; i < this.Steps - 1; i++)
        {
            var dd0 = _filtered_distance[i];
            var dd1 = _filtered_distance[i + 1];
            var dd = dd1 - dd0;
            if (dd > _distanceGap)
            {
                if (count == 0)
                {
                    cp = CalcPos(i);
                    sp = cp;
                    ++count;
                }
            }
            else if (dd < -_distanceGap)
            {
                if (count > _minSize && count < _maxSize)
                {
                    Vector4 rp = cp / (float)count; // center of object
                    rp.w = (CalcPos(i) - sp).magnitude; // size estimate
                    _objs.Add(rp);
                }
                count = 0;
            }
            else
            {
                if (count > 0)
                {
                    cp += CalcPos(i);
                    ++count;
                }
            }
        }
    }

    private void getDataWork()
    {
        var time_stamp = 0L;
        while (_isRun)
        {
            var receive_data = read_line(_stream);
            SCIP_Reader.MD(receive_data, ref time_stamp, ref _read_data);
            if (_read_data.Count == 0) continue;

            lock (_lockobj)
            {
                _read_data.CopyTo(_distance);
                GetObjs();
            }
        }
    }

    public void CloseStream()
    {
        _isRun = false;
        _thread.Join();

        write(_stream, SCIP_Writer.QT());    // stop measurement mode
        read_line(_stream); // ignore echo back
        _stream.Close();
        _urg.Close();
    }

    /// <summary>
    /// Read to "\n\n" from NetworkStream
    /// </summary>
    /// <returns>receive data</returns>
    private static string read_line(NetworkStream stream)
    {
        if (stream.CanRead)
        {
            StringBuilder sb = new StringBuilder();
            bool is_NL2 = false;
            bool is_NL = false;
            do
            {
                char buf = (char)stream.ReadByte();
                if (buf == '\n')
                {
                    if (is_NL)
                    {
                        is_NL2 = true;
                    }
                    else
                    {
                        is_NL = true;
                    }
                }
                else
                {
                    is_NL = false;
                }
                sb.Append(buf);
            } while (!is_NL2);

            return sb.ToString();
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// write data
    /// </summary>
    private static bool write(NetworkStream stream, string data)
    {
        if (stream.CanWrite)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            stream.Write(buffer, 0, buffer.Length);
            return true;
        }
        else
        {
            return false;
        }
    }
}