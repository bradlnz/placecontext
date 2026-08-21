# PlaceContext test job — Ruby runtime smoke test.
# Sandbox contract: shard payload JSON on stdin, result JSON on stdout, logs on stderr.
require "json"

raw = $stdin.read
input = raw.strip.empty? ? {} : JSON.parse(raw)

$stderr.puts "hello from ruby"
$stdout.write JSON.generate({ runtime: "ruby", ok: true, input: input })
