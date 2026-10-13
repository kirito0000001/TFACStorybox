"""在线执行一个 Unreal Python 任务。

这个脚本**不跑在编辑器里**，而是跑在引擎自带的 Python 上（`<引擎>/Binaries/ThirdParty/
Python3/Win64/python.exe`）。它通过 Unreal 的 Python 远程执行协议连到**已经在运行**的
编辑器，把真正的任务脚本塞进去执行。

和离线路（`UnrealEditor-Cmd.exe -ExecutePythonScript=...`）相比：编辑器开着时快约十倍，
而且能读到离线实例读不到的那类资产（WidgetBlueprint / MetaSoundSource —— 实测同一工程
在线 23 项 0 错、离线 4 项全报"未找到"）。

连不上编辑器时抛出带 `GAL_REMOTE_UNAVAILABLE` 标记的错：工具箱靠这个标记区分
「连不上编辑器」（可以退回离线执行）和「脚本自己失败」（退回去也是一样的错，
白等一次编辑器冷启动）。标记字符串必须和
Services/UnrealSync/UnrealPythonTaskExecutionService.cs 里的常量一致。
"""

import argparse
import json
import os
import sys
import time


def _normalized(path):
    return os.path.normcase(os.path.abspath(path or "").rstrip("\\/"))


def _engine_identity(path):
    """把 <UE> 和 <UE>/Engine 两种写法归一成同一个身份。

    引擎根目录这个值，上报方给的是哪一种跟版本有关：编辑器节点那边可能报 <UE>，
    而工具箱这边（从 .uproject 的 EngineAssociation 反查出来的）给的是 <UE>/Engine。
    直接比字符串就永远对不上 —— 而**对不上的后果是静默退回离线**：
    慢十倍、还不报错，只看现象根本想不到是这里。

    2026-10-13 实测踩到：原来只试了 {engine_root, engine_root/Engine} 两种，
    少了最关键的 <UE> 那一种。
    """
    normalized = _normalized(path)
    if os.path.basename(normalized).lower() == "engine":
        normalized = os.path.dirname(normalized)
    return normalized


def _load_job(path):
    with open(path, "r", encoding="utf-8") as source:
        job = json.load(source)
    if int(job.get("protocolVersion", 0)) != 1:
        raise RuntimeError("unsupported remote Unreal job protocol")
    return job


def _find_matching_node(remote, project_path, engine_root, timeout_seconds):
    """找一个「工程和引擎都对得上」的编辑器节点。

    只认进程名是不够的：开着**别的**工程时也会连上，然后在执行阶段整个失败。
    所以这里要求 project_root 与 engine_root 同时匹配。

    引擎根目录按**身份**比（[`_engine_identity`](run_remote_unreal_job.py)），
    而不是比字符串 —— 上报方给 <UE> 还是 <UE>/Engine 跟版本有关，
    不该由这条判断去赌。
    """
    expected_project_root = _normalized(os.path.dirname(project_path))
    expected_engine_identity = _engine_identity(engine_root)
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        for node in remote.remote_nodes:
            if (_normalized(node.get("project_root")) == expected_project_root and
                    _engine_identity(node.get("engine_root")) == expected_engine_identity):
                return node
        time.sleep(0.2)
    return None


def _print_remote_output(result):
    for item in result.get("output", []):
        output = item.get("output", "")
        if output:
            print(output, end="" if output.endswith("\n") else "\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--job", required=True)
    parser.add_argument("--timeout", type=float, default=6.0)
    args = parser.parse_args()

    job_path = os.path.abspath(args.job)
    job = _load_job(job_path)
    engine_root = os.path.abspath(job["engineRoot"])

    remote_module_candidates = [
        os.path.join(engine_root, "Engine", "Plugins", "Experimental", "PythonScriptPlugin", "Content", "Python"),
        os.path.join(engine_root, "Plugins", "Experimental", "PythonScriptPlugin", "Content", "Python"),
    ]
    remote_module_path = next(
        (path for path in remote_module_candidates
         if os.path.isfile(os.path.join(path, "remote_execution.py"))),
        None)
    if remote_module_path is None:
        raise RuntimeError("Unreal Python remote_execution.py was not found under engine root: " + engine_root)

    sys.path.insert(0, remote_module_path)
    try:
        import remote_execution
    finally:
        sys.path.remove(remote_module_path)

    remote = remote_execution.RemoteExecution()
    remote.start()
    try:
        node = _find_matching_node(remote, job["projectPath"], engine_root, args.timeout)
        if node is None:
            # 这个标记让工具箱能把「连不上编辑器」和「脚本自己失败」区分开：
            # 前者可以退回离线执行，后者退回去也是一样的错。
            raise RuntimeError(
                "GAL_REMOTE_UNAVAILABLE "
                "No running Unreal Editor with Python Remote Execution enabled matches "
                "the selected project and engine. The target Editor may also be busy running "
                "another remote Python task. Enable Project Settings > Plugins > Python > "
                "Enable Remote Execution, then wait for any current task to finish.")

        bootstrap_path = os.path.join(
            os.path.dirname(os.path.abspath(__file__)),
            "execute_remote_unreal_job.py")
        command = (
            "import runpy\n"
            "runpy.run_path({!r}, init_globals={{'GAL_REMOTE_JOB_PATH': {!r}}})"
        ).format(bootstrap_path, job_path)
        remote.open_command_connection(node["node_id"])
        result = remote.run_command(
            command,
            unattended=True,
            exec_mode=remote_execution.MODE_EXEC_FILE)
        _print_remote_output(result)
        if not result.get("success", False):
            raise RuntimeError(str(result.get("result", "remote Unreal command failed")))
        return 0
    finally:
        remote.stop()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print("Remote Unreal execution failed: {}".format(error), file=sys.stderr)
        raise SystemExit(1)
