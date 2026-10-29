import json, sys, time

def read():
    return json.loads(sys.stdin.readline())

def send(value):
    print(json.dumps(value), flush=True)

def reply(request, result):
    send({'id': request['id'], 'result': result})

def call(id, name, args):
    send({'id': id, 'method': 'item/tool/call', 'params': {
        'threadId': 'test-thread', 'callId': str(id), 'tool': name, 'arguments': args}})

mode = sys.argv[1]
request = read()
assert request['method'] == 'initialize' and request['params']['capabilities']['experimentalApi']
reply(request, {})
assert read()['method'] == 'initialized'
request = read()
if mode in ('models', 'signedout'):
    assert request['method'] == 'account/read' and request['params']['refreshToken'] is False
    reply(request, {'account': None if mode == 'signedout' else {'type': 'chatgpt'}, 'requiresOpenaiAuth': True})
    if mode == 'signedout':
        time.sleep(30)
        sys.exit()
    request = read()
    assert request['method'] == 'model/list' and request['params']['includeHidden'] is False
    reply(request, {'data': [{'model': 'other'}, {'model': 'preferred', 'isDefault': True}], 'nextCursor': 'page2'})
    request = read()
    assert request['params']['cursor'] == 'page2'
    reply(request, {'data': [{'model': 'other'}, {'model': 'last'}], 'nextCursor': None})
    time.sleep(30)
    sys.exit()
assert request['method'] == 'config/read'
reply(request, {'config': {'mcp_servers': {'private_server': {'enabled': True}}}})
request = read()
assert request['method'] == 'thread/start'
params = request['params']
assert params['config']['mcp_servers.private_server.enabled'] is False
assert params['environments'] == [] and params['sandbox'] == 'read-only'
assert params['approvalPolicy'] == 'never' and params['ephemeral'] is True
assert params['model'] == 'test-model' and params['dynamicTools'][0]['name'] == 'guest_shell'
assert params['baseInstructions'] == 'Guest tools only.'
reply(request, {'thread': {'id': 'test-thread'}})
request = read()
assert request['method'] == 'turn/start'
assert request['params']['input'][0]['text'] == 'Check the desktop.'
assert request['params']['input'][1]['url'] == 'data:image/png;base64,first'
reply(request, {'turn': {'id': 'test-turn'}})
if mode == 'answer':
    send({'method': 'item/completed', 'params': {'item': {'type': 'agentMessage', 'text': 'A real answer.'}}})
    send({'method': 'turn/completed', 'params': {'turn': {'status': 'completed'}}})
    time.sleep(30)
elif mode == 'cancel':
    time.sleep(30)
elif mode == 'approval':
    send({'id': 99, 'method': 'item/commandExecution/requestApproval', 'params': {}})
    time.sleep(30)
elif mode == 'failed':
    send({'method': 'turn/completed', 'params': {'turn': {'status': 'failed', 'error': {'message': 'secret upstream details'}}}})
    time.sleep(30)
else:
    call(100, 'guest_shell', {'command': 'echo HELLO', 'description': 'Check guest'})
    output = read()
    assert output['id'] == 100 and output['result']['contentItems'][0]['text'] == 'HELLO'
    call(101, 'desktop_screenshot', {})
    output = read()
    assert output['id'] == 101
    assert output['result']['contentItems'][1] == {'type': 'inputImage', 'imageUrl': 'data:image/png;base64,second'}
    call(102, 'finish', {'success': True, 'summary': 'Verified'})
    time.sleep(30)
