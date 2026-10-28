"""Reproduce review findings without Unity, source edits, or replacing old evidence."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(name, command):
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                            encoding="utf-8", errors="replace")
    (HERE / (name + ".log")).write_text(result.stdout + result.stderr, encoding="utf-8")
    return {"exit_code": result.returncode, "log": name + ".log"}


def main():
    if (HERE / "results.json").exists():
        raise SystemExit("Preserve this review run; choose a new evidence directory for another run.")
    source_paths = sorted(p for p in (ROOT / "Assets").rglob("*")
                          if p.is_file() and p.suffix in {".cs", ".asmdef", ".shader", ".compute"})
    source_paths += sorted((ROOT / "Tools").glob("*.py"))
    identities = {p.relative_to(ROOT).as_posix(): sha(p) for p in source_paths}
    (HERE / "source-sha256.json").write_text(json.dumps(identities, indent=2) + "\n", encoding="utf-8")
    results = {"generated_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "scope": "Static review and isolated Windows .NET8 checks; no Unity control or native acceptance"}
    results["project_default_encoding"] = run("project-default-encoding", [sys.executable, str(ROOT / "Tools/check_project.py")])
    for label, tool in [("project_utf8", "check_project.py"), ("documentation", "check_documentation.py"),
                        ("release_freshness", "check_exploration_release.py")]:
        results[label] = run(label, [sys.executable, "-X", "utf8", str(ROOT / "Tools" / tool)])
    failures = (HERE / "documentation.log").read_text(encoding="utf-8").splitlines()
    results["documentation"]["failure_lines"] = sum(line.startswith("FAIL ") for line in failures)
    results["documentation"]["failures_outside_preimages"] = [line for line in failures if line.startswith("FAIL ") and "preimage" not in line.lower()]
    dotnet = Path(shutil.which("dotnet")).resolve()
    version = lambda p: tuple(int(v) if v.isdigit() else 0 for v in p.name.split("."))
    sdk = sorted((dotnet.parent / "sdk").glob("8.*"), key=version)[-1]
    refs = sorted((dotnet.parent / "packs/Microsoft.NETCore.App.Ref").glob("8.*"), key=version)[-1] / "ref/net8.0"
    results["sdk"] = sdk.name
    with tempfile.TemporaryDirectory(prefix="ashen-code-review-") as folder:
        work = Path(folder)

        def compile(label, sources):
            dll = work / (label + ".dll")
            quote = lambda p: '"' + str(p) + '"'
            args = ["-nologo", "-warnaserror+", "-langversion:9.0", "-optimize+", "-target:exe", "-out:" + quote(dll)]
            args += ["-r:" + quote(p) for p in sorted(refs.glob("*.dll"))]
            args += [quote(p) for p in sources]
            response = work / (label + ".rsp")
            response.write_text("\n".join(args), encoding="utf-8")
            result = run(label, [str(dotnet), str(sdk / "Roslyn/bincore/csc.dll"), "@" + str(response)])
            result["sources"] = [p.relative_to(ROOT).as_posix() for p in sources]
            return dll, result

        core = sorted((ROOT / "Assets/AshenVoxel/Core").rglob("*.cs"))
        sources = core + [ROOT / "Assets/_Validation/FiniteMatterSlice/Runtime/LocalizedMatter.cs",
                          ROOT / "Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs", HERE / "PhysicsDebtProbe.cs"]
        dll, results["physics_compile"] = compile("physics-compile", sources)
        if results["physics_compile"]["exit_code"] == 0:
            dll.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": "8.0.0"}}}), encoding="utf-8")
            results["physics_probe"] = run("physics-probe", [str(dotnet), str(dll)])

        # Exact current source selection from run_exploration_smoke_checks.py.
        smoke = core + sorted((ROOT / "Assets/AshenVoxel/WorldScaleData").glob("*.cs"))
        smoke += [ROOT / "Assets/AshenVoxel/WorldScaleRuntime" / name for name in
                  ["WorldExplorer.cs", "WorldExplorerIntegration.cs", "WorldExplorerVoxels.cs", "RemoteTerrainField.cs", "VisualWater.cs"]]
        smoke += sorted((ROOT / "Assets/AshenVoxel/Exploration").glob("*.cs")) + sorted((ROOT / "Tools/ExplorationSmokeChecks").glob("*.cs"))
        _, results["smoke_compile_only"] = compile("smoke-compile-only", smoke)
        # The old runtime/evidence-writing stage is deliberately not invoked.
    results["sources_unchanged"] = all(sha(p) == identities[p.relative_to(ROOT).as_posix()] for p in source_paths)
    (HERE / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    for key, value in results.items():
        if isinstance(value, dict):
            print(key, json.dumps({k: v for k, v in value.items() if k not in {"sources", "failures_outside_preimages"}}))
    print((HERE / "physics-probe.log").read_text(encoding="utf-8") if (HERE / "physics-probe.log").exists() else "Physics compile failed; inspect log")
    print("All inspected source identities unchanged:", results["sources_unchanged"])


if __name__ == "__main__":
    main()
