-- Simauria: reglas de mensajes (port de ProcessRules\CSimauria.cs del cliente original).
--
-- Dos clases de mensaje, y solo el PRIMERO del bloque (desde el se toma hasta el final):
--  1. Canales: una linea que empieza por "[" cuando la linea ANTERIOR del bloque esta vacia y
--     queda algun "]" de ahi en adelante. El mensaje es todo lo que queda del bloque.
--  2. Conversacion: "X dice: '...", "X te dice: '...", "Dices: '..." y "Dijiste a X: ...".
--     Para "X te dice:" y "Dijiste a" el mensaje es todo lo que queda del bloque; para los otros
--     dos se recorta en el ULTIMO apostrofo de lo que queda del bloque.
--
-- Contrato: el script lee om.lines (tabla, empieza en 1) y llama a om.message(texto).

local lines = om.lines
local n = #lines

-- Las cuatro primeras palabras separadas por UN espacio (String.Split(' ') del original).
local WORDS = [[^([^ ]*) ([^ ]*)(?: ([^ ]*))?(?: ([^ ]*))?]]

local function q(word)
  return word ~= nil and string.sub(word, 1, 1) == "'"
end

-- Lo que queda del bloque desde la linea i, sin las lineas vacias del final.
local function rest(i)
  local last = n
  while last > i and lines[last] == "" do
    last = last - 1
  end
  return table.concat(lines, "\n", i, last)
end

-- true si hay un "]" en la linea i o en cualquiera de las siguientes
local function bracketAhead(i)
  for k = i, n do
    if string.find(lines[k], "]", 1, true) then return true end
  end
  return false
end

-- Posicion del ultimo apostrofo del texto, o nil
local function lastApostrophe(text)
  local found = nil
  local from = 1
  while true do
    local at = string.find(text, "'", from, true)
    if not at then return found end
    found = at
    from = at + 1
  end
end

for i = 1, n do
  local line = lines[i]

  if i > 1 and lines[i - 1] == "" and string.sub(line, 1, 1) == "[" and bracketAhead(i) then
    om.message(rest(i))
    return
  end

  local w = om.match(line, WORDS)
  if w then
    local w1, w2, w3, w4 = w[1], w[2], w[3], w[4]
    local talk = (w2 == "dice:" and q(w3))
              or (w2 == "te" and w3 == "dice:" and q(w4))
              or (w1 == "Dices:" and q(w2))
              or (w1 == "Dijiste" and w2 == "a" and w4 ~= nil and w3 ~= nil and string.sub(w3, -1) == ":")
    if talk then
      local message = rest(i)
      if w2 ~= "a" and w2 ~= "te" then
        local cut = lastApostrophe(message)
        if cut then message = string.sub(message, 1, cut) end
      end
      om.message(message)
      return
    end
  end
end
