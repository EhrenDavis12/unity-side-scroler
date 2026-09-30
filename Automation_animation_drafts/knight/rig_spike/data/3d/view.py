import sys, base64, subprocess, os
glb, out = sys.argv[1], sys.argv[2]; yaw=float(sys.argv[3]) if len(sys.argv)>3 else 0
b64=base64.b64encode(open(glb,"rb").read()).decode()
html=f"""<!doctype html><html><body style="margin:0;background:#191923">
<script type="importmap">{{"imports":{{"three":"https://cdn.jsdelivr.net/npm/three@0.169.0/build/three.module.js","three/addons/":"https://cdn.jsdelivr.net/npm/three@0.169.0/examples/jsm/"}}}}</script>
<script type="module">
import * as THREE from 'three'; import {{GLTFLoader}} from 'three/addons/loaders/GLTFLoader.js';
const r=new THREE.WebGLRenderer({{antialias:true,preserveDrawingBuffer:true}}); r.setSize(800,800); r.setClearColor(0x191923); document.body.appendChild(r.domElement);
r.outputColorSpace=THREE.SRGBColorSpace;
const s=new THREE.Scene(); s.add(new THREE.AmbientLight(0xffffff,1.6)); const d=new THREE.DirectionalLight(0xffffff,1.6); d.position.set(1,2,3); s.add(d);
const bin=Uint8Array.from(atob("{b64}"),c=>c.charCodeAt(0)).buffer;
new GLTFLoader().parse(bin,'',g=>{{const m=g.scene; const box=new THREE.Box3().setFromObject(m); const c=box.getCenter(new THREE.Vector3()); const z=box.getSize(new THREE.Vector3());
 m.position.sub(c); const piv=new THREE.Group(); piv.add(m); piv.rotation.y={yaw}*Math.PI/180; s.add(piv);
 const h=Math.max(z.x,z.y,z.z); const cam=new THREE.PerspectiveCamera(30,1,0.01,100); cam.position.set(0,0,h*2.2); cam.lookAt(0,0,0);
 r.render(s,cam); document.title='done';}});
</script></body></html>"""
open(out+".html","w").write(html)
subprocess.run(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome","--headless=new","--disable-gpu-sandbox","--use-angle=metal","--enable-webgl","--ignore-gpu-blocklist",
 "--window-size=800,800","--virtual-time-budget=15000",f"--screenshot={out}","file://"+os.path.abspath(out+".html")],capture_output=True,timeout=120)
print("ok",out)
