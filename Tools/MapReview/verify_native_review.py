"""Read-only round-trip checks for the editable Blender review file."""
import bpy
import hashlib
import json
import sys
from pathlib import Path

args=sys.argv[sys.argv.index('--')+1:]
source_path=Path(args[0]).resolve()
review_path=Path(args[1]).resolve()
output_path=Path(args[2]).resolve()

def snapshot(scene):
    bpy.context.window.scene=scene
    bpy.context.view_layer.update()
    anchors={obj.name:[list(row) for row in obj.matrix_world] for obj in scene.objects if obj.name.startswith('ANCHOR_')}
    portal=[list(row) for row in scene.objects['Portal - carved solid ring'].matrix_world]
    geometry={obj.name:(len(obj.data.vertices),len(obj.data.polygons)) for obj in scene.objects if obj.type=='MESH'}
    return dict(anchors=anchors,portal=portal,geometry=geometry)

bpy.ops.wm.open_mainfile(filepath=str(source_path),load_ui=False)
source=snapshot(bpy.data.scenes['Map_Rebuild_20260924'])
bpy.ops.wm.open_mainfile(filepath=str(review_path),load_ui=False)
retained=snapshot(bpy.data.scenes['Map_Rebuild_20260924'])
if source!=retained:
    print('ANCHORS_MATCH',source['anchors']==retained['anchors'])
    print('PORTAL_MATCH',source['portal']==retained['portal'])
    print('GEOMETRY_MATCH',source['geometry']==retained['geometry'])
    for name,value in source['anchors'].items():
        if value!=retained['anchors'].get(name):print('ANCHOR_DIFFERENCE',name,value,retained['anchors'].get(name))
    if source['portal']!=retained['portal']:print('PORTAL_DIFFERENCE',source['portal'],retained['portal'])
    raise RuntimeError('The retained source scene does not match the source file.')
full=bpy.data.scenes.get('V2 Environment Review')
if full is not None:
    bpy.context.window.scene=full;bpy.context.view_layer.update()
    delivered={obj.get('canonical_name',obj.name):obj for obj in full.objects}
    for name,matrix in source['anchors'].items():
        if [list(row) for row in delivered[name].matrix_world]!=matrix:
            raise RuntimeError('Full-review original anchor drift: '+name)
    if [list(row) for row in delivered['Portal - carved solid ring'].matrix_world]!=source['portal']:
        raise RuntimeError('Full-review portal matrix drift.')
    if len([name for name in delivered if name.startswith('ANCHOR_Node_')])!=14:
        raise RuntimeError('Full-review anchor count is not fourteen.')
scenes=[]
for scene in bpy.data.scenes:
    if not scene.name.startswith('V2 '):continue
    bpy.context.window.scene=scene
    entries=[]
    for obj in scene.objects:
        if obj.type!='MESH':continue
        evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
        entries.append(dict(name=obj.name,vertices=len(mesh.vertices),triangles=len(mesh.loop_triangles),
                            materials=[slot.material.name if slot.material else None for slot in obj.material_slots]))
        evaluated.to_mesh_clear()
    scenes.append(dict(scene=scene.name,meshes=entries,totalTriangles=sum(e['triangles'] for e in entries)))
report=dict(source=str(source_path),review=str(review_path),retainedSourceMatches=True,
            originalNodeCount=len(source['anchors']),originalPortalMatrixUnchanged=True,
            sourceFileSha256=hashlib.sha256(source_path.read_bytes()).hexdigest(),
            reviewedScenes=scenes,visualStatus='Full geometry candidate; final visual acceptance requires current actual Unity camera review')
output_path.write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='reviewedScenes'},indent=2))
