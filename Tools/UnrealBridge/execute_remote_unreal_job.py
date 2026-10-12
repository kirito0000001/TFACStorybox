"""在线执行时，**在编辑器进程内部**跑的那一段（由 run_remote_unreal_job.py 引导进来）。

它只做三件事：读作业文件 → 把作业里带的环境变量临时设上 → 用 runpy 跑真正的任务脚本。

为什么环境变量要"设上再还原"：这一步跑在**用户已经开着的编辑器进程**里，
`os.environ` 是那个进程的全局状态。不改回去就等于往用户会话里漏环境变量，
下一次别的地方读到它会莫名其妙。
"""

import json
import os
import runpy


job_path = globals().get("GAL_REMOTE_JOB_PATH", "")
if not job_path or not os.path.isfile(job_path):
    raise RuntimeError("remote Unreal job file does not exist")

with open(job_path, "r", encoding="utf-8") as source:
    job = json.load(source)
if int(job.get("protocolVersion", 0)) != 1:
    raise RuntimeError("unsupported remote Unreal job protocol")

script_path = os.path.abspath(job.get("scriptPath", ""))
if not os.path.isfile(script_path):
    raise RuntimeError("remote Unreal task script does not exist: " + script_path)

environment = dict(job.get("environment", {}))
previous_environment = {key: os.environ.get(key) for key in environment}
try:
    for key, value in environment.items():
        os.environ[str(key)] = str(value)
    runpy.run_path(script_path, run_name="__main__")
finally:
    for key, value in previous_environment.items():
        if value is None:
            os.environ.pop(key, None)
        else:
            os.environ[key] = value
