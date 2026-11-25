# After `/graphify . --update`: re-point C# `using` edges to the real namespace nodes.
# graphify links them only among the files re-extracted in that run, so namespaces declared
# in unchanged files get a stray stand-in node. ponytail: uses graphify private helpers (0.9.73); recheck on upgrade.
# Run from the repo root: python tools/graphify-relink-csharp.py  (then `graphify export html`)
import json, collections
from pathlib import Path
from graphify.extractors.csharp import _resolve_cross_file_csharp_imports
from graphify.build import build_from_json
from graphify.cluster import score_all
from graphify.analyze import god_nodes, surprising_connections, suggest_questions
from graphify.report import generate
from graphify.export import to_json
from graphify.detect import detect
g = json.loads(Path('graphify-out/graph.json').read_text(encoding='utf-8'))
nodes, edges = g['nodes'], g.get('links') or g.get('edges')
n0 = len(nodes)
_resolve_cross_file_csharp_imports([], [], nodes, edges)
real = {n['label'].lower().replace('.', '_') for n in nodes if n.get('type') == 'namespace'}
linked = {e['source'] for e in edges} | {e['target'] for e in edges}
# edgeless stand-ins for a namespace the repo declares: the resolver above only drops ones it re-pointed
nodes[:] = [n for n in nodes if not (not n.get('source_file') and n['id'] in real and n['id'] not in linked)]
print(f'nodes {n0} -> {len(nodes)}')
G = build_from_json({'nodes': nodes, 'edges': edges, 'hyperedges': g.get('hyperedges', [])}, root='.', directed=False)
com = collections.defaultdict(list)
for n, d in G.nodes(data=True): com[int(d['community'])].append(n)
com = dict(com)
labels = {int(k): v for k, v in json.loads(Path('graphify-out/.graphify_labels.json').read_text(encoding='utf-8')).items() if int(k) in com}
coh = score_all(G, com)
det = detect(Path('.'))
rep = generate(G, com, coh, labels, god_nodes(G), surprising_connections(G, com), det, {'input':0,'output':0}, '.', suggested_questions=suggest_questions(G, com, labels))
assert to_json(G, com, 'graphify-out/graph.json', force=True, community_labels=labels)
Path('graphify-out/GRAPH_REPORT.md').write_text(rep, encoding='utf-8')
print(f'Graph: {G.number_of_nodes()} nodes, {G.number_of_edges()} edges, {len(com)} communities')
