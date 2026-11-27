# 3D-Modelle & Lizenzen (`Models/`)

Alle 24 RTS-Gebäude-Assets (8 Typen × Level 1–3) und das Transport-Kit wurden als **eigene Original-Blender-Assets** (Blender 5.3 PBR, glTF 2.0 Binary ohne Draco, eingebettete sRGB-PNG-Texturen, abgestimmt auf *Nuclear Option* Unity URP) erstellt. Die 24 Gebäude-Compounds wurden im echten Vanilla-Maßstab (**1:1 Blender-Meter, `targetMaxDimensionMeters: 0`**) aus `Models_RTS_Buildings/RTS_Buildings_VanillaStyle.blend` exportiert:

| Slot-ID | Gebäude / Objekt | Level 1 / Basis (`B × T × H`) | Level 2 (`_lvl2.glb`) | Level 3 (`_lvl3.glb`) | Tris (`L1 / L2 / L3`) | Quelle / Lizenz |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- |
| `command_hq` | Kommandozentrale (`Command HQ`) | `command_hq.glb` (`85,3 × 78,5 × 24,6 m`) | `command_hq_lvl2.glb` (`99,3 × 88,5 × 22,5 m`) | `command_hq_lvl3.glb` (`115,3 × 98,5 × 52,8 m`) | `4.576 / 4.544 / 7.700` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `barracks` | Kaserne (`Barracks`) | `barracks.glb` (`97,3 × 72,5 × 16,2 m`) | `barracks_lvl2.glb` (`111,3 × 74,5 × 19,5 m`) | `barracks_lvl3.glb` (`125,3 × 78,5 × 27,6 m`) | `4.040 / 6.660 / 8.056` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `pilot_academy` | Piloten-Akademie (`Pilot Academy`) | `pilot_academy.glb` (`107,3 × 78,8 × 19,0 m`) | `pilot_academy_lvl2.glb` (`125,3 × 86,8 × 20,0 m`) | `pilot_academy_lvl3.glb` (`143,3 × 94,8 × 56,0 m`) | `3.448 / 6.132 / 7.828` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `logistics_camp` | Militärlager (`Logistics Camp`) | `logistics_camp.glb` (`75,3 × 86,5 × 19,0 m`) | `logistics_camp_lvl2.glb` (`83,3 × 92,5 × 20,0 m`) | `logistics_camp_lvl3.glb` (`91,3 × 98,5 × 21,5 m`) | `4.784 / 7.236 / 9.064` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `naval_command` | Marine-Kommando (`Naval Command`) | `naval_command.glb` (`73,3 × 100,8 × 27,9 m`) | `naval_command_lvl2.glb` (`77,3 × 112,8 × 40,2 m`) | `naval_command_lvl3.glb` (`83,3 × 124,8 × 51,6 m`) | `3.268 / 5.548 / 6.884` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `abm_radar` | Frühwarnradar (`ABM Radar`) | `abm_radar.glb` (`69,3 × 74,8 × 33,6 m`) | `abm_radar_lvl2.glb` (`73,3 × 78,8 × 42,6 m`) | `abm_radar_lvl3.glb` (`77,3 × 82,8 × 62,3 m`) | `2.068 / 3.476 / 5.172` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `abm_silo` | VLS-Abfangsilo (`ABM Silo`) | `abm_silo.glb` (`69,3 × 74,8 × 18,0 m`) | `abm_silo_lvl2.glb` (`75,3 × 76,8 × 23,0 m`) | `abm_silo_lvl3.glb` (`83,3 × 80,8 × 21,2 m`) | `2.856 / 4.240 / 4.324` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `abm_plant` | Abfangraketen-Fabrik (`ABM Plant`) | `abm_plant.glb` (`113,3 × 84,5 × 20,0 m`) | `abm_plant_lvl2.glb` (`135,3 × 98,8 × 26,5 m`) | `abm_plant_lvl3.glb` (`157,3 × 110,9 × 68,0 m`) | `7.100 / 11.148 / 14.532` | Original-Asset (`RTS_Buildings_VanillaStyle.blend`, CC0 1.0) |
| `sam_kit` | 20ft NATO ISO Transport-Kit (`VL-49 Tarantula`) | `sam_kit.glb` (`6,12 × 2,47 × 2,75 m`) | — | — | `1.092` | Original-Asset (`RTS_Buildings.blend`, CC0 1.0) |

---

## Eigene 3D-Modelle tauschen (Drop-in-Anleitung)

Du kannst jedes Modell jederzeit durch ein eigenes Modell ersetzen — ohne das Plugin neu zu kompilieren:

1. **Format:** Entweder **`.glb` (glTF 2.0 binary ohne Draco-Kompression)** oder **`.obj`** (optional mit `.mtl` und `.png`/`.jpg`-Texturen).
2. **Ablage & Level-Varianten:** Kopiere deine Dateien direkt nach `BepInEx\plugins\GroundControlRtsExpanded\Models\`. Basis-Slot `<slot>.glb` (Level 1) wird automatisch durch `<slot>_lvl2.glb` und `<slot>_lvl3.glb` ergänzt, sofern vorhanden.
3. **Skalierung & Ausrichtung:** In `models.json` steht `targetMaxDimensionMeters: 0` für native **1:1-Blender-Meter-Skalierung** (oder ein positiver Meterwert zum Normieren der längsten Grundseiten-Achse); `autoGround: true` setzt das Modell automatisch sauber auf Bodenhöhe `y = 0`. Mit `rotationEuler` (z. B. `[0, 90, 0]`) lässt sich das Modell drehen. Mit `"tintHex": null` und `"smoothnessOverride": null` werden die PBR-Werte der `.glb` 1:1 übernommen.
4. **Wichtiger Export-Hinweis (Blender / Sketchfab):**
   - Bei `.glb`: In Blender beim Export unter *Geometry -> Compression* **Draco deaktivieren** (`KHR_draco_mesh_compression` wird zur Laufzeit nicht entpackt) und *Images: Automatic* (PNG/JPG, kein WebP) belassen.
   - Bei `.obj`: Dreiecke/Quads werden automatisch trianguliert, Texturen über `map_Kd` in der `.mtl` geladen.
