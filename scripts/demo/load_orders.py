#!/usr/bin/env python3
"""Page the deterministic 100k PostgreSQL fixture into the real AI workflow queue."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import time
import urllib.error
import urllib.request

SOURCE = 'load-test-orders-v1'
TERMINAL = {'Succeeded', 'Failed', 'Expired', 'Cancelled', 'Partial'}


def encode(value):
    return json.dumps(value, separators=(',', ':'), allow_nan=False).encode()


def prepare(records, first, last):
    if [r['order_id'] for r in records] != [f'LOAD-{i:06d}' for i in range(first, last + 1)]:
        raise ValueError('Source page is incomplete or out of order')
    payload = {'sourceId': SOURCE, 'sourceName': 'PostgreSQL load test', 'records': records}
    enriched = [{**r, 'input_source_id': SOURCE, 'input_source_name': payload['sourceName']} for r in records]
    # Leave room for validation metadata and the workflow envelope below the AI's 65,536-char limit.
    if len(encode({**payload, 'records': enriched})) > 60_000:
        raise ValueError('Batch exceeds the AI context budget; reduce --batch-size')
    body = encode(payload)
    key = f'{SOURCE}:{first}:{last}:' + hashlib.sha256(body).hexdigest()[:24]
    return body, key


def read_page(first, last):
    sql = f"""BEGIN READ ONLY; SET LOCAL ROLE source_reader;
SELECT coalesce(json_agg(row_to_json(r) ORDER BY order_id), '[]') FROM
(SELECT order_id,status,currency,total_cents,
 to_char(placed_at AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"') AS placed_at
 FROM orders_load_test WHERE order_id BETWEEN 'LOAD-{first:06d}' AND 'LOAD-{last:06d}') r;
COMMIT;"""
    result = subprocess.run(['docker', 'exec', 'placecontext-source-demo', 'psql', '-X', '-qAt',
                             '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'source_demo', '-c', sql],
                            check=True, capture_output=True, text=True, timeout=30)
    return json.loads(result.stdout)


def save(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def request(opener, token, url, body=None, key=None):
    headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'}
    if key:
        headers['Idempotency-Key'] = key
    for attempt in range(5):
        try:
            with opener.open(urllib.request.Request(url, body, headers), timeout=60) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code not in (429, 502, 503, 504) or attempt == 4:
                raise
        except (urllib.error.URLError, TimeoutError):
            if attempt == 4:
                raise
        time.sleep(2 ** attempt)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--end', type=int, required=True, help='Last fixture ID to process, inclusive')
    parser.add_argument('--batch-size', type=int, default=250)
    parser.add_argument('--inflight', type=int, default=4)
    parser.add_argument('--phase', required=True)
    parser.add_argument('--token-file', type=pathlib.Path, required=True)
    parser.add_argument('--state', type=pathlib.Path, default=pathlib.Path('dist/demo-source/load-test.json'))
    args = parser.parse_args()
    if not 1 <= args.end <= 100000 or not 1 <= args.batch_size <= 250 or not 1 <= args.inflight <= 32:
        parser.error('Require end 1..100000, batch-size 1..250, inflight 1..32')
    config = json.loads(pathlib.Path('dist/demo-source/validation-pipeline.json').read_text())
    url = 'http://localhost:7700/api/v1/queues/' + config['queueId'] + '/messages'
    token = args.token_file.read_text().strip()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    state = json.loads(args.state.read_text()) if args.state.exists() else {'queueId': config['queueId'], 'batches': [], 'phases': []}
    if state['queueId'] != config['queueId']:
        raise ValueError('Checkpoint belongs to another queue')
    batches = state['batches']
    if any(b.get('receipt', {}).get('status') in TERMINAL - {'Succeeded'} for b in batches):
        raise ValueError('Failed batches require investigation before continuing')
    phase = {'name': args.phase, 'started': time.time(), 'batchSize': args.batch_size, 'inflight': args.inflight, 'end': args.end}
    state['phases'].append(phase)
    save(args.state, state)
    last_print = 0
    while True:
        pending = [b for b in batches if b.get('receipt', {}).get('status') not in TERMINAL]
        for batch in pending:
            if 'receipt' not in batch:
                body, key = prepare(read_page(batch['first'], batch['last']), batch['first'], batch['last'])
                if key != batch['key']:
                    raise ValueError('Source changed since checkpoint; refusing duplicate submission')
                batch['receipt'] = request(opener, token, url, body, key)
            else:
                batch['receipt'] = request(opener, token, url + '/' + batch['receipt']['trackingId'])
            if batch['receipt']['status'] in TERMINAL:
                batch['observedFinished'] = time.time()
            save(args.state, state)
        failed = [b for b in batches if b.get('receipt', {}).get('status') in TERMINAL - {'Succeeded'}]
        blocked = [b for b in batches if b.get('receipt', {}).get('status') in ('AwaitingInput', 'Waiting')]
        pending = [b for b in batches if b.get('receipt', {}).get('status') not in TERMINAL]
        next_id = batches[-1]['last'] + 1 if batches else 1
        while not failed and not blocked and next_id <= args.end and len(pending) < args.inflight:
            last = min(args.end, next_id + args.batch_size - 1)
            body, key = prepare(read_page(next_id, last), next_id, last)
            batch = {'first': next_id, 'last': last, 'key': key, 'phase': args.phase, 'submitted': time.time()}
            batches.append(batch)
            save(args.state, state)  # Persist the idempotency key BEFORE sending, including on restart.
            batch['receipt'] = request(opener, token, url, body, key)
            save(args.state, state)
            pending.append(batch)
            next_id = last + 1
        if time.time() - last_print >= 30 or not pending:
            done = sum(b['last'] - b['first'] + 1 for b in batches if b.get('receipt', {}).get('status') == 'Succeeded')
            print(json.dumps({'phase': args.phase, 'completed': done, 'submittedThrough': next_id - 1,
                              'inflight': len(pending), 'failedBatches': len(failed), 'elapsed': round(time.time() - phase['started'], 1)}), flush=True)
            last_print = time.time()
        if blocked or (failed and not pending):
            raise RuntimeError('Pipeline stopped: inspect receipts in checkpoint')
        if not pending and next_id > args.end:
            phase['finished'] = time.time()
            save(args.state, state)
            return
        if pending and time.time() - min(b['submitted'] for b in pending) > 1200:
            raise TimeoutError('Batch has not completed within 20 minutes; checkpoint preserved')
        time.sleep(3)


if __name__ == '__main__':
    main()
