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

## Referencia física y presentación

El entorno representa una maqueta real. Se conservan los materiales y texturas
originales de cajas, racks, líneas de producción, AGV y cuarto. No se sustituyen
por colores planos ni se cambia su acabado para estilizar la presentación.
`SimulationAppearance` ajusta únicamente la iluminación y el fondo de cámara.

El panel de misiones ocupa una columna fuera de la vista de la cámara, con
controles en una franja inferior. Las alarmas usan un triángulo de advertencia y
el texto «Temporalmente fuera de servicio» en el panel; no son obstáculos físicos.

El cuarto y los docks están guardados en `Assets/Scenes/SampleScene.unity` y
se ven sin entrar en Play. `Cube` utiliza la malla persistente
`Assets/SimulationWarehouse/RoomWithDockOpenings.asset`; conserva los materiales
y UV del cuarto. Los portones, camiones y tapetes son objetos editables de la escena.

La fachada une los portones mediante paños de pared y dinteles. El cuarto termina
en ese plano, los cuerpos de camión quedan detrás y los tapetes hacia el almacén.
Las entradas permanecen abiertas y los pallets entregados descansan en el interior.

Los componentes reutilizan la geometría guardada al iniciar Play, sin duplicarla.
Los nuevos materiales de los docks están en `Assets/SimulationWarehouse`.
Las texturas originales de cajas y racks se conservan.

## Ejecución desde Unity (PULL local)

En Play, **Ejecutar Python** solicita una corrida al servidor local. Si no está
activo, Unity intenta arrancarlo con el intérprete configurado en
`SimulationSessionUI.pythonExecutable`; busca primero `.venv` y, en este equipo,
el entorno de pruebas existente. Para preparar otro equipo:

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r SimulationPython/requirements.txt
.venv/bin/python SimulationPython/unity_server.py
```

En Windows el intérprete es `.venv/Scripts/python.exe`. El arranque automático
requiere la carpeta `SimulationPython` al lado de `Assets` (Unity Editor).
En una aplicación compilada se puede iniciar el servidor por separado.
La API escucha exclusivamente en `127.0.0.1:8765`:
`POST /runs` recibe `{"seed":42,"steps":800,"seed_count":20}`, `GET /runs/{id}` indica el estado,
y `GET /runs/{id}/export` entrega el historial y los resultados.
Solo admite una corrida simultánea; comunica errores y limita el tamaño de entrada.

Python calcula baseline/BFS y propuesta/A* con la misma semilla. Unity visualiza
la propuesta; **Resultados** compara las 13 métricas finales de ambas estrategias.
El proceso es PULL por corrida completa, no transmisión de agentes en tiempo real.
La interfaz permanece operativa mientras Python calcula.

**Reproducir / Pausa**, **Inicio** y la barra de pasos permiten recorrer el historial
hacia delante y atrás. Mover la barra pausa la reproducción. Los resultados son
finales, independientemente del paso visualizado. Cada descarga válida queda en
`Application.persistentDataPath/last_simulation.json` y se reutiliza al abrir.
Las cámaras 1–5 se conservan.

`m4_final_model.py` procede del `m4_final.py` entregado: se retiraron el comando
de instalación de Colab, las importaciones de presentación y las ejecuciones del
notebook. `unity_server.py` graba todos los pasos y añade ubicación/nivel y retiro
visual de pallets. La navegación incorpora los márgenes de seguridad descritos abajo. Las métricas se calculan
con su función `compute_metrics`. El archivo original de Downloads no se modifica.

La navegación versión 2 amplía los obstáculos 17 unidades (carrocería y margen),
separa los puntos de servicio y verifica cada segmento completo, incluidos los
desvíos. La pared de los portones limita el área transitable. Baseline y propuesta
usan la misma geometría segura y sus métricas se recalculan; ya no son las cifras
del notebook que modelaba AGV como puntos. Unity rechaza exportaciones PULL antiguas
sin esos márgenes y usa el JSON actualizado del proyecto.

Validación: prueba Python de historial/métricas y prueba de integración Unity que
descarga por HTTP, comprueba posiciones contra Python, retrocede, pausa y vuelve
a cargar sin duplicar AGV ni dejar pallets de otros pasos.

## Actualización m4_final (1).py

La comparación del código ejecutable (ignorando comentarios/docstrings) confirmó
que las clases de agentes, las rutas y el esquema del exportador no cambiaron.
Cambió el experimento: promedios de 20 semillas; la animación usa la primera.
Se eliminó la métrica Reasignaciones y se añadió color de alarma a Matplotlib.
Unity ya representa alarmas y conserva los materiales originales.

El servidor admite `seed_count` entre 1 y 20 (clientes antiguos que lo omiten
siguen recibiendo una corrida). Unity solicita 20 por defecto.
Cada clic en **Ejecutar Python** elige una semilla nueva. Para repetir un experimento,
desactiva `randomizeSeed` en SimulationSessionUI y configura `seed`.
La semilla inicial 42 produce semillas 42..61, reproducibles; se pueden configurar
`seed`, `steps` y `seedCount` en SimulationSessionUI. Se usa módulo 1000000
al generar la secuencia. No se concatena el historial de distintas corridas:
los frames y meta corresponden a la primera; results contiene el promedio,
seed_count y la lista exacta de semillas. La página identifica ambos alcances.
Las correcciones de navegación versión 2 permanecen en ambos métodos.

El Python de Downloads se conserva intacto. Solo se incorporan los cambios de
comportamiento relevantes en la adaptación existente para no perder la seguridad
geométrica ni volver a introducir las celdas de instalación/animación de Colab.

## Navegación versión 3: bloqueos entre vehículos

El replanteo considera a los otros AGV como obstáculos y comprueba los segmentos
completos. Si el destino está temporalmente cerrado, busca un lugar libre para
ceder el paso y conserva el destino original de la misión. Se elimina el reinicio
por longitud del camino, que antes repetía la misma ruta bloqueada. Las corridas
anteriores se invalidan. La prueba de regresión reproduce el atasco de semilla 42,
comprueba progreso y limita la detención continua de AGV con misión activa.

## Interfaz de resultados y baterías

Resultados tiene fondo opaco y dos pestañas: mejora porcentual y valores originales.
El signo sigue el criterio del notebook (menos tiempo, distancia, carga y conflictos
es mejor); si baseline es cero, se muestra N/D. No se mezclan porcentajes con
valores absolutos. El panel de flota permanece visible con las cuatro baterías
del frame actual, también al pausar, retroceder y abrir Resultados.

## Navegación y energía versión 4

La recarga interrumpe cualquier misión al llegar al 30 %, conservando su asignación
y el pallet transportado. Tras alcanzar el 80 %, el AGV retoma la misión.
Puede solicitar recarga antes del 30 % si la distancia real al cargador requiere
más energía y reserva. Se calcula una ruta BFS a cada cargador disponible y se
escoge una alcanzable, no el más cercano en línea recta. Los desvíos de recarga
también deben caber en el presupuesto de batería. Esperar un cargador no consume
energía de movimiento; la batería solo aumenta físicamente sobre el cargador.

El JSON distingue charge_phase: TO_CHARGER, WAITING y CHARGING. Unity muestra
Ruta, Espera o Carga respectivamente. Los JSON anteriores se invalidan para
evitar reproducir baterías agotadas de corridas antiguas. Las métricas se vuelven
a calcular bajo esta misma política para baseline y propuesta.

## Biblioteca de simulaciones

**Simulaciones → Nueva semilla** genera y descarga en segundo plano. La reproducción,
las cámaras y la barra de pasos siguen disponibles al volver. La corrida nueva no
reemplaza la actual: pulsa **Abrir** en la biblioteca y luego **Reproducir**.
Los JSON descargados se conservan en `Application.persistentDataPath/runs`; se
recupera también `last_simulation.json` al abrir la biblioteca por primera vez.
Cada tarjeta muestra semilla, fecha, pasos, cantidad de semillas comparadas y tamaño.

El progreso de cálculo cuenta pasos reales de baseline y propuesta para todas las
semillas. La descarga muestra bytes recibidos y porcentaje según Content-Length.
La validación y el guardado se realizan en segundo plano; archivos incompletos no
aparecen en la lista. El servidor escribe sus exportaciones en `server-runs` cuando
lo inicia Unity. Reinicia los servidores anteriores para habilitar el porcentaje de
cálculo; sin ese dato se muestra un indicador de actividad, sin inventar porcentajes.
