"""Resume the same bake after memory waits, preserving every attempt's evidence."""
from pathlib import Path
import argparse
import datetime
import json
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--job', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--first-attempt', type=int, required=True)
    args = parser.parse_args()
    command = [sys.executable, '-X', 'utf8', str(Path(__file__).with_name('bake_coarse_overview.py')),
               '--job', str(args.job), '--output', str(args.output), '--batch', '8']
    attempt = args.first_attempt
    while True:
        record = args.evidence / f'attempt-{attempt}.json'
        if record.exists():
            raise FileExistsError('Attempt evidence already exists: ' + str(record))
        started = datetime.datetime.now(datetime.timezone.utc).isoformat()
        with (args.output / f'attempt-{attempt}.log').open('w', encoding='utf-8') as log:
            run = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
        state = json.loads((args.output / 'progress.json').read_text())
        state['attempt_started_utc'] = started
        state['attempt_finished_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        state['process_exit_code'] = run.returncode
        record.write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'attempt': attempt, 'status': state['status'],
                          'completed_blocks': state['completed_blocks']}), flush=True)
        if run.returncode or state['status'] not in ('PASS', 'MEMORY_WAIT'):
            raise RuntimeError('Bake stopped; inspect preserved attempt log and report')
        if state['status'] == 'PASS':
            break
        time.sleep(10)  # Native bake retains its unchanged 4/2 GiB memory guards.
        attempt += 1


if __name__ == '__main__':
    main()
