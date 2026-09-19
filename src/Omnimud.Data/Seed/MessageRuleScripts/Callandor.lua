-- Callandor: reglas de mensajes (port de ProcessRules\CCallandor.cs del cliente original).
--
-- Un mensaje empieza en una linea que cumple alguna de las condiciones de abajo y SIGUE en las
-- lineas siguientes del bloque hasta la que termina en comilla simple (la comilla de cierre
-- seguida de salto de linea). Si la comilla no se cierra dentro del bloque, el mensaje es todo
-- lo que queda del bloque, como en el original.
--
-- Contrato: el script lee om.lines (tabla, empieza en 1) y llama a om.message(texto).

local lines = om.lines

-- Lo que dice el propio jugador: basta con que la linea EMPIECE asi.
local OWN = [[^(?:Solicitas '|Instruyes|Dices '|Charlas '|Transmites a |<Comunicas> '|Susurras '|Gritas '|Conspiras '|Gruñes '|Transmites cariñosamente a )]]

-- Las seis primeras palabras, separadas por UN espacio (como String.Split(' ') del original:
-- dos espacios seguidos dan una palabra vacia).
local WORDS = [[^([^ ]*) ([^ ]*) ([^ ]*)(?: ([^ ]*))?(?: ([^ ]*))?(?: ([^ ]*))?]]

-- true si la palabra existe y empieza por comilla simple
local function q(word)
  return word ~= nil and string.sub(word, 1, 1) == "'"
end

local function startsMessage(line)
  if om.match(line, OWN) then return true end
  -- Todas las demas condiciones piden una palabra que empiece por comilla.
  if not string.find(line, " '", 1, true) then return false end

  local w = om.match(line, WORDS)
  if not w then return false end
  local w1, w2, w3, w4, w5, w6 = w[1], w[2], w[3], w[4], w[5], w[6]

  return (w2 == "te" and w3 == "transmite" and q(w4))
      or (w2 == "dice" and w3 == "al" and w4 == "grupo" and q(w5))
      or (w2 == "gruñe" and w3 == "al" and w4 == "grupo" and q(w5))
      or ((w2 == "dice" or w2 == "charla" or w2 == "conspira" or w2 == "comunica" or w2 == "gruñe") and q(w3))
      or (w2 == "susurra" and q(w3))
      or (w2 == "grita" and w3 == "cerca" and w4 == "de" and w5 == "aqui" and q(w6))
      or (w2 == "grita" and q(w3))
      or (w2 == "dice" and w3 == "al" and w4 == "equipo" and q(w5))
      or (string.sub(w1, 1, 1) == "<" and string.sub(w1, -1) == ">" and w2 == "conversa" and q(w3))
      or (w2 == "te" and w3 == "transmite" and w4 == "cariñosamente" and q(w5))
      or (w2 == "solicita" and q(w3))
      or (w1 == "Trivial:" and q(w3))
end

local i, n = 1, #lines
while i <= n do
  local line = lines[i]
  if line ~= "" and startsMessage(line) then
    -- Hasta la linea que acaba en comilla, o hasta el final del bloque.
    local last = i
    while last < n and string.sub(lines[last], -1) ~= "'" do
      last = last + 1
    end
    -- Sin comilla de cierre no cuentan las lineas vacias del final.
    while last > i and lines[last] == "" do
      last = last - 1
    end
    om.message(table.concat(lines, "\n", i, last))
    i = last + 1
  else
    i = i + 1
  end
end
