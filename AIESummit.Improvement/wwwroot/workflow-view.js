export function workflowEdges(graph, nodes = graph?.nodes || graph?.steps || []) {
  const edges = new Map();
  function add(from, to, pageSelection = false) {
    if (!from || !to) return;
    const key = JSON.stringify([from, to]);
    const previous = edges.get(key);
    edges.set(key, { from, to, pageSelection: pageSelection || !!previous?.pageSelection });
  }
  for (const edge of graph?.edges || []) add(edge.from, edge.to, edge.relation === 'selected_pages');
  for (const node of nodes) {
    const to = node.key || node.id;
    for (const from of node.dependsOn || []) add(from, to);
    if (node.selectedPagesFrom) add(node.selectedPagesFrom, to, true);
  }
  return [...edges.values()];
}
