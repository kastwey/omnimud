-- Cyberlife: reglas de mensajes (port de ProcessRules\CCyberlife.cs del cliente original).
--
-- Las 15 expresiones regulares del original, literales y en su orden, sin distinguir mayusculas.
-- Cada linea del bloque que cumple alguna es un mensaje. (El original las probaba contra el
-- bloque entero y se quedaba solo con la primera coincidencia; aqui no se pierde ninguna.)
--
-- Contrato: el script lee om.lines (tabla, empieza en 1) y llama a om.message(texto).

local PATTERNS = {
  [[^(Murmuras|Dices) con acento .+?, ".+?"$]],
  [[^(Murmuras|Dices): ".+?"$]],
  [[^gritas: ".+?"$]],
  [[^.+? grita: ".+?"$]],
  [[^.+? grita cerca de aquí: ".+?"$]],
  [[^\[.+?\] .+?: ".+?"$]],
  [[^\[.+?:\] ".+?"$]],
  [[^.+? (Murmura|Dice) con acento .+?, ".+?"$]],
  [[^.+? (Murmura|Dice): ".+?"$]],
  [[^".+? chatea: ".+?"$]],          -- sic: la comilla inicial esta en el original
  [[^Transmites a .+?, ".+?"$]],
  [[^.+? te transmite, ".+?"$]],
  [[^\*{2,} .+? Ha solicitado asistencia con el siguiente motivo: .+?\*{2,}$]],
  [[^.+?(te)? dice por teléfono, ".+?"$]],
  [[^dices por teléfono, ".+?"$]],
}

-- Una sola expresion con las 15 alternativas en su orden: se evalua una vez por linea.
local ANY = "(?i)(?:" .. table.concat(PATTERNS, "|") .. ")"

for _, line in ipairs(om.lines) do
  -- Todas las expresiones piden unas comillas dobles, menos la de asistencia (asteriscos).
  if string.find(line, '"', 1, true) or string.find(line, "**", 1, true) then
    local m = om.match(line, ANY)
    if m then om.message(m[0]) end
  end
end
