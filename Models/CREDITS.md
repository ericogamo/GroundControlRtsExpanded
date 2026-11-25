# 3D-Modelle & Lizenzen (`Models/`)

Alle 9 RTS-Gebäude und Transport-Assets wurden als **eigene Original-Blender-Assets** (Blender 5.3 PBR, glTF 2.0 Binary ohne Draco, eingebettete sRGB-PNG-Texturen, abgestimmt auf *Nuclear Option* `0.34.6` Unity URP) erstellt und aus der Master-Szene `Models_RTS_Buildings/RTS_Buildings.blend` exportiert:

| Slot-ID | Gebäude / Objekt | Lokale Datei | Maße (`B × T × H`) | Tris | Quelle / Lizenz |
| :--- | :--- | :--- | :--- | ---: | :--- |
| `command_hq` | Kommandozentrale (`Command HQ`) | `command_hq.glb` | `28,0 × 21,5 × 17,5 m` | `2.236` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `barracks` | Kaserne (`Barracks`) | `barracks.glb` | `24,0 × 17,5 × 11,3 m` | `1.768` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `pilot_academy` | Piloten-Akademie (`Pilot Academy`) | `pilot_academy.glb` | `30,0 × 21,0 × 13,6 m` | `2.924` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `logistics_camp` | Militärlager (`Logistics Camp`) | `logistics_camp.glb` | `26,0 × 20,0 × 11,3 m` | `1.628` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `naval_command` | Marine-Akademie (`Naval Command`) | `naval_command.glb` | `26,0 × 19,5 × 17,2 m` | `1.898` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `abm_radar` | Phased-Array-Frühwarnradar (`ABM Radar`) | `abm_radar.glb` | `22,0 × 19,0 × 19,7 m` | `2.622` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `abm_silo` | VLS-Abfangsilo (`ABM Silo`) | `abm_silo.glb` | `20,0 × 19,1 × 5,2 m` | `2.202` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `abm_plant` | Abfangraketen-Fabrik (`ABM Interceptor Plant`) | `abm_plant.glb` | `32,0 × 22,0 × 13,8 m` | `1.848` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |
| `sam_kit` | 20ft NATO ISO Transport-Kit (`VL-49 Tarantula`) | `sam_kit.glb` | `6,12 × 2,47 × 2,75 m` | `1.092` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0 / Projekt-Original) |

---

## Eigene 3D-Modelle tauschen (Drop-in-Anleitung)

Du kannst jedes Modell jederzeit durch ein eigenes Modell ersetzen — ohne das Plugin neu zu kompilieren:

1. **Format:** Entweder **`.glb` (glTF 2.0 binary ohne Draco-Kompression)** oder **`.obj`** (optional mit `.mtl` und `.png`/`.jpg`-Texturen).
2. **Ablage:** Kopiere deine Datei direkt nach `BepInEx\plugins\GroundControlRtsExpanded\Models\`, z. B. `barracks.glb` oder `barracks.obj` (und passe bei anderem Dateinamen ggf. `file` in `models.json` an).
3. **Skalierung & Ausrichtung:** In `models.json` legt `targetMaxDimensionMeters` fest, wie groß die längste Seite im Spiel in Metern ist; `autoGround: true` setzt das Modell automatisch sauber auf Bodenhöhe `y = 0`. Mit `rotationEuler` (z. B. `[0, 90, 0]`) lässt sich das Modell drehen. Mit `"tintHex": null` und `"smoothnessOverride": null` werden die PBR-Werte der `.glb` 1:1 übernommen.
4. **Wichtiger Export-Hinweis (Blender / Sketchfab):**
   - Bei `.glb`: In Blender beim Export unter *Geometry -> Compression* **Draco deaktivieren** (`KHR_draco_mesh_compression` wird zur Laufzeit nicht entpackt) und *Images: Automatic* (PNG/JPG, kein WebP) belassen.
   - Bei `.obj`: Dreiecke/Quads werden automatisch trianguliert, Texturen über `map_Kd` in der `.mtl` geladen.
