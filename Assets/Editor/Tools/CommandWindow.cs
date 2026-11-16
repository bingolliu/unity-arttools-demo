using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

public class CommandWindow : EditorWindow
{
    public float windowHeight = 500f; // 窗口高度
    private static StringBuilder _output = new StringBuilder();
    private static Action<string> _outPutCallback;
    private static Action _endCallback;
    private static Action<string> _errorCallback;
    private Vector2 _scrollPosition = Vector2.zero;
    private static string _preOutPut = "";

    public static void Execute(string command, Action<string> outPutCallback = null, Action endCallback = null, Action<string> errorCallback = null)
    {
        var window = GetWindow<CommandWindow>();
        window.minSize = new Vector2(300, window.windowHeight); // 设置窗口最小尺寸
        window.Show();

        _output.Clear();
        _outPutCallback = outPutCallback;
        _endCallback = endCallback;
        _errorCallback = errorCallback;
        window.ExecuteCommand(command);
    }

    public void ExecuteCommand(string command)
    {
        var processInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        var process = new Process
        {
            StartInfo = processInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendOutput(">> " + e.Data);
                _outPutCallback?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendOutput("ERROR>> " + e.Data);
                _errorCallback?.Invoke(e.Data);
            }
        };

        process.Exited += (sender, e) =>
        {
            AppendOutput("END", true);
            process.Close();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public void AppendOutput(string text, bool isExit = false)
    {
        //确保在主线程中调用
        EditorApplication.delayCall += () =>
        {
            _output.AppendLine(text);
            Repaint();

            if (isExit)
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.delayCall += () =>
                {
                    _endCallback?.Invoke();
                };
            }
        };
    }

    private void OnGUI()
    {
        var style = EditorStyles.wordWrappedLabel;
        var outPut = _output.ToString();
        var content = new GUIContent(outPut);

        // 计算文本的高度
        var height = style.CalcHeight(content, position.width);

        // 如果文本的高度大于滚动视图的高度，那么就滚动到底部
        if (!_preOutPut.Equals(outPut) && height > position.height)
        {
            _scrollPosition.y = height - position.height;
            _preOutPut = outPut;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(windowHeight));
        EditorGUILayout.LabelField(content, style, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}