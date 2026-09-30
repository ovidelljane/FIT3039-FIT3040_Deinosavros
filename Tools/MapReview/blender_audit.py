"""Read-only source inspection. Reports are generated artifacts, not scene edits."""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

output = Path(sys.argv[sys.argv.index('--') + 1])
output.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
records = []
for obj in scene.objects:
    corners = [obj.matrix_world @ Vector(p) for p in obj.bound_box]
    records.append(dict(name=obj.name, type=obj.type,
                        collections=[c.name for c in obj.users_collection],
                        location=list(obj.location), rotation=list(obj.rotation_euler), scale=list(obj.scale),
                        matrix=[list(row) for row in obj.matrix_world],
                        bounds=[list(min(p[i] for p in corners) for i in range(3)),
                                list(max(p[i] for p in corners) for i in range(3))],
                        hidden=obj.hide_render, vertices=len(obj.data.vertices) if obj.type == 'MESH' else 0,
                        polygons=len(obj.data.polygons) if obj.type == 'MESH' else 0,
                        materials=[s.material.name if s.material else None for s in obj.material_slots]))
report = dict(source=bpy.data.filepath, scene=scene.name, camera=scene.camera.name if scene.camera else None,
              engine=scene.render.engine, objects=records)
(output / 'blender-baseline.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
for rec in records:
    if not rec['name'].startswith(('RU Rooted', 'RU Cliff', 'RU Colonnade rooted', 'Paved route', 'Node terrace')):
        print(rec['name'], rec['collections'], rec['bounds'])
