# Reproducción M4 en Unity

Abre `Assets/Scenes/SampleScene.unity` y pulsa **Play**. `SimulationManager`
contiene las referencias ya configuradas. Al comenzar, los 15 modelos se
acomodan al JSON; al salir de Play vuelven a sus posiciones de edición.
Activa **Gizmos** en Scene para ver el layout de referencia.

La geometría y las trayectorias comparten `CoordinateMapper`:

- Python `(x, y)` → Unity `(x, altura del piso, z)`.
- Escala uniforme: `0.19`. El entorno ocupa `85.5 × 57` unidades Unity.
- Origen XZ: `(-37.75, -38.5)`. Piso Y: `-0.8351`.
- El interior del cuarto existente va de X `-40` a `50`, y Z `-40` a `20`.
- Cada rack/dock/línea se ajusta por los límites visibles de sus meshes,
  no por el pivote del prefab. Conserva su altura y orientación original.
- Los puntos `stations.pos` son puntos de atención, separados del centro visual.
- La diagonal del AGV mide `26` unidades Python (`4.94` en Unity); la caja mide
  `10` (`1.9` en Unity).
- Las rutas reservan `17` unidades alrededor de zonas y paredes para el vehículo
  con carga. Los puntos de atención están a `22` unidades del borde y los centros
  de los AGV se separan al menos `32` unidades en el control de movimiento.
- `AGV_DIAMETER` y `PALLET_DIAMETER` se exportan como `agv_diameter` y
  `pallet_diameter`. Regenera el JSON al cambiar estas dimensiones.
- Cada caja exporta `storage_zone` mientras está almacenada/reservada/entregada.
  Unity la apoya sobre la estación correspondiente, separada del punto del pasillo
  donde se detiene el AGV. En producción descansa sobre los rodillos; los racks
  reciben dos repisas durante Play; `storage_level` selecciona la inferior (0) o superior (1). Los docks usan su plataforma interior.
- `removed` oculta una caja cuando el camión la retira. Las cajas de entrada o con
  una misión pendiente no pueden despacharse, y un pallet no se asigna a dos
  misiones abiertas. Así no aparecen cajas duplicadas sobre una misma estación.
- Al estar en transporte, la caja sigue sobre las horquillas del vehículo asignado.
  La validación comprueba también que el volumen de la carga quepa en el margen
  utilizado por el planificador.

## Regenerar el JSON

`m4_simulation.py` es una copia ejecutable del modelo de `m4.py` proporcionado.
No incluye las instalaciones de Colab ni las celdas de gráficos/comparaciones.
El archivo original en Descargas no se modifica.

```sh
python3 -m venv .venv
.venv/bin/pip install -r SimulationPython/requirements.txt
.venv/bin/python SimulationPython/m4_simulation.py
```

El comando escribe directamente `Assets/StreamingAssets/simulation_export.json`.
La estrategia por defecto es `proposed`, con 4 AGV, 800 pasos y semilla 42.
Puedes cambiar `--seed`, `--steps`, `--strategy` o `--output`.

Esta copia exporta **cada paso** (`FRAME_SAMPLE = 1`). También corrige las
maniobras de desbloqueo: conecta el desvío por la cuadrícula y comprueba el
segmento completo con margen para la huella del AGV. Estos cambios pueden
alterar misiones y tiempos respecto de una ejecución del modelo anterior.
La escena usa `secondsPerFrame = 0.05`, conservando 20 pasos por segundo.

Unity reproduce las posiciones del archivo; no ejecuta Python en vivo. Para
ver una nueva ejecución, regenera el JSON y vuelve a entrar en Play. Un JSON
antiguo con tramos que cruzan racks usa saltos entre muestras en esos tramos
y emite un aviso; no es posible recuperar los giros omitidos por ese archivo.

## Comprobaciones

```sh
.venv/bin/python -m unittest discover -s SimulationPython -v
```

En Unity: **Simulation → Validate JSON and scene**. La validación usa una escena
de previsualización y comprueba referencias, dimensiones, piso, puntos de
atención, rotación, pivotes y trayectorias. No guarda cambios en la escena.

## Frente, ruedas y carga del AGV

El prefab `Assets/AGV.prefab` incluye `AgvMechanics` con referencias explícitas a
las cuatro ruedas y a `Cube (3)` (horquillas). El frente original es +X y se
corrige a +Z antes de centrar el modelo. El cilindro vertical no gira.

Las ruedas giran alrededor del centro de su malla, según distancia y giro del
vehículo; no giran por el salto al reiniciar la reproducción. Las horquillas
suben durante 0.3 segundos de reproducción al cargar y bajan al entregar.
La caja sigue un punto de apoyo sobre las horquillas, utilizando la asociación
pallet/AGV de la misión del JSON. Al entregar se coloca sobre la superficie de la estación registrada.
Estas animaciones no cambian la trayectoria ni los tiempos de la simulación.
La validación de Unity también comprueba estas articulaciones y que la carga
quede dentro del margen de navegación exportado por Python.


## Obstáculos y niveles

Las cajas alternan de forma estable entre los dos niveles de los racks según su
identificador. Se mantiene una caja almacenada por estación: los dos niveles
son opciones de colocación, no un aumento de la capacidad logística del modelo.

El JSON incluye `pedestrians` en cada frame y `pedestrian_radius` en metadata.
Unity dibuja cada peatón con chaleco/casco naranja y un círculo en el suelo que
muestra el radio de bloqueo utilizado por Python. Aparece y desaparece con el
evento; no es un obstáculo adicional inventado en Unity. Los estados
`OUT_OF_SERVICE` en `stations` encienden una señal roja encima de la estación.

Con vehículos mayores, la separación y los márgenes de navegación también
crecen; pueden cambiar los tiempos y el número de misiones completadas.

## Cámaras

Al entrar en Play se muestra el almacén desde arriba. Las teclas **1, 2, 3 y 4**
seleccionan una vista en tercera persona detrás de AGV-1, AGV-2, AGV-3 y AGV-4.
**5** vuelve a la vista cenital ortográfica, con X hacia la derecha e Y de Python
hacia arriba. Funcionan los números de la fila superior y del teclado numérico.
Haz clic dentro de la pestaña **Game** para que Unity reciba las teclas.

El controlador utiliza la cámara principal existente. El seguimiento se actualiza
tras el movimiento de los AGV y evita interponer paredes/racks entre cámara y
vehículo. La vista superior se adapta a la proporción de la ventana. La distancia,
altura y suavizado se pueden ajustar en `SimulationCameraController`, que se
agrega automáticamente a `SimulationManager` al iniciar la reproducción.
