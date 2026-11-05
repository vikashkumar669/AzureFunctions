--[[
  book.lua – Pandoc filter for the Azure Functions for D365 book.

  1. Pulls code listings from the compiled repository by region marker
     (// <listing:name> … // </listing:name>) and numbers them.
  2. Turns // <1> callout markers into ❶ ❷ ….
  3. Numbers figures, tables and screenshot placeholders per chapter
     ("Figure 6.1") and resolves @fig:id / @lst:id / @tbl:id references.
  4. Renders callouts, screenshot placeholders and the chapter opener
     for HTML (print PDF) and DOCX (named Word styles).
]]

local stringify = pandoc.utils.stringify
local is_docx = FORMAT:match("docx") ~= nil

local chapter = "0"
local code_root = "."
local meta_cache = nil
local counters = { fig = 0, lst = 0, tbl = 0 }
local labels = {}
local names = { fig = "Figure", lst = "Listing", tbl = "Table" }
local markers = { "❶", "❷", "❸", "❹", "❺", "❻", "❼", "❽", "❾" }

local callout_titles = {
  tip = "Tip",
  note = "Note",
  warning = "Warning",
  security = "Security",
  performance = "Performance",
  objectives = "In this chapter you will",
  summary = "Key takeaways",
  exercise = "Hands-on exercise",
  ["knowledge-check"] = "Knowledge check",
}

local function md_inlines(text)
  if not text or text == "" then return pandoc.Inlines({}) end
  local doc = pandoc.read(text, "markdown")
  return pandoc.utils.blocks_to_inlines(doc.blocks)
end

local function next_label(kind, id)
  counters[kind] = counters[kind] + 1
  local label = names[kind] .. " " .. chapter .. "." .. counters[kind]
  if id and id ~= "" then labels[id] = label end
  return label
end

local function label_inlines(label, rest)
  local out = pandoc.Inlines({
    pandoc.Strong({ pandoc.Str(label) }),
    pandoc.Space(), pandoc.Str("–"), pandoc.Space(),
  })
  out:extend(rest)
  return out
end

local function styled(blocks, style)
  if is_docx then
    return pandoc.Div(blocks, pandoc.Attr("", {}, { ["custom-style"] = style }))
  end
  return pandoc.Div(blocks)
end

-- Code listings -----------------------------------------------------

local function read_region(path, region)
  local file = io.open(code_root .. "/" .. path, "r")
  if not file then
    error("Listing file not found: " .. path)
  end
  local text = file:read("a")
  file:close()

  local open_tag = "// <listing:" .. region .. ">"
  local close_tag = "// </listing:" .. region .. ">"
  local s = text:find(open_tag, 1, true)
  local e = text:find(close_tag, 1, true)
  if not s or not e then
    error("Region '" .. region .. "' not found in " .. path)
  end

  local body = text:sub(s + #open_tag, e - 1)
  body = body:gsub("^[^\n]*\n", "")   -- rest of the opening line
  body = body:gsub("\n[ \t]*$", "")   -- indentation before close tag

  local lines, min = {}, nil
  for line in (body .. "\n"):gmatch("([^\n]*)\n") do
    if not line:find("// </?listing:") then
      table.insert(lines, line)
      if line:match("%S") then
        local indent = #line:match("^(%s*)")
        if not min or indent < min then min = indent end
      end
    end
  end
  min = min or 0
  for i, line in ipairs(lines) do
    lines[i] = line:sub(min + 1)
  end
  return table.concat(lines, "\n")
end

local function apply_markers(code)
  -- "   // <1>" at the end of a line becomes "  ❶"
  return (code:gsub("%s*//%s*<(%d)>", function(n)
    return "  " .. (markers[tonumber(n)] or ("(" .. n .. ")"))
  end))
end

local function code_block(cb)
  local include = cb.attributes["include"]
  if include then
    local region = cb.attributes["region"]
    if region then
      cb.text = read_region(include, region)
    else
      -- No region: the whole file (used for JSON, which has no comments).
      local file = io.open(code_root .. "/" .. include, "r")
      if not file then error("Listing file not found: " .. include) end
      cb.text = file:read("a"):gsub("\n+$", "")
      file:close()
    end
    cb.attributes["include"] = nil
    cb.attributes["region"] = nil
  end
  cb.text = apply_markers(cb.text)

  local caption = cb.attributes["caption"]
  if not caption and not cb.identifier:match("^lst:") then
    return cb, false
  end

  local label = next_label("lst", cb.identifier)
  local id = cb.identifier
  cb.identifier = ""
  cb.attributes["caption"] = nil
  if include and not cb.classes:includes("numberLines") then
    cb.classes:insert("numberLines")
  end

  local cap = label_inlines(label, md_inlines(caption))
  if include then
    cap:insert(pandoc.LineBreak())
    cap:insert(pandoc.Code(include))
  end
  local cap_block = styled({ pandoc.Para(cap) }, "Listing Caption")
  if not is_docx then cap_block.classes:insert("listing-caption") end

  return pandoc.Div({ cap_block, cb },
    pandoc.Attr(id, { "listing" })), false
end

-- Figures -----------------------------------------------------------

local function figure(fig)
  local id = fig.identifier
  local first_img = nil
  fig = fig:walk({
    Image = function(img)
      first_img = first_img or img
      if is_docx then
        img.src = img.src:gsub("%.svg$", ".png")
      end
      return img
    end,
  })
  if id == "" and first_img and first_img.identifier ~= "" then
    id = first_img.identifier
  end
  local label = next_label("fig", id)
  local long = fig.caption.long
  if #long > 0 and (long[1].t == "Plain" or long[1].t == "Para") then
    long[1].content = label_inlines(label, long[1].content)
  else
    fig.caption.long = { pandoc.Plain(label_inlines(label, {})) }
  end
  return fig, false
end

-- Divs: tables, screenshot placeholders, callouts -------------------

local function table_div(div)
  local label = next_label("tbl", div.identifier)
  local cap = label_inlines(label, md_inlines(div.attributes["caption"]))
  div.attributes["caption"] = nil
  local cap_block = styled({ pandoc.Para(cap) }, "Table Caption")
  if not is_docx then cap_block.classes:insert("table-caption") end
  local content = pandoc.Blocks({ cap_block })
  content:extend(div.content)
  return pandoc.Div(content,
    pandoc.Attr(div.identifier, { "table-wrap" })), false
end

local function capture_div(div)
  local a = div.attributes
  local label = next_label("fig", div.identifier)

  local box = pandoc.Blocks({
    pandoc.Para(pandoc.Inlines({
      pandoc.Strong({ pandoc.Str("ILLUSTRATIVE PLACEHOLDER") }),
      pandoc.Space(),
      pandoc.Str("–"), pandoc.Space(),
    }) .. md_inlines(
      "not a product screenshot. To be captured before typesetting.")),
  })
  if a["app"] then
    box:insert(pandoc.Para(pandoc.Inlines({
      pandoc.Strong({ pandoc.Str("Application:") }), pandoc.Space(),
    }) .. md_inlines(a["app"])))
  end
  if a["path"] then
    box:insert(pandoc.Para(pandoc.Inlines({
      pandoc.Strong({ pandoc.Str("Screen:") }), pandoc.Space(),
    }) .. md_inlines(a["path"])))
  end
  if a["annotations"] then
    local items = {}
    for item in a["annotations"]:gmatch("[^;]+") do
      local n, text = item:match("^%s*(%d)%s+(.-)%s*$")
      if n then
        table.insert(items, { pandoc.Plain(pandoc.Inlines({
          pandoc.Str(markers[tonumber(n)] or n), pandoc.Space(),
        }) .. md_inlines(text)) })
      end
    end
    box:insert(pandoc.Para({ pandoc.Strong({ pandoc.Str("Annotations:") }) }))
    box:insert(pandoc.BulletList(items))
  end

  local box_div = styled(box, "Capture Box")
  if not is_docx then box_div.classes:insert("capture-box") end

  local cap = label_inlines(label, md_inlines(a["caption"]))
  local cap_div = styled({ pandoc.Para(cap) }, "Image Caption")
  if not is_docx then cap_div.classes:insert("capture-caption") end

  return pandoc.Div({ box_div, cap_div },
    pandoc.Attr(div.identifier, { "capture" })), false
end

local function callout_div(div, class)
  if not is_docx then return nil end
  local style = class:gsub("^%l", string.upper):gsub("%-(%l)", function(c)
    return " " .. c:upper()
  end)
  local label = styled({ pandoc.Para({ pandoc.Str(callout_titles[class]) }) },
    style .. " Label")
  local body = styled(div.content, style .. " Body")
  return pandoc.Div({ label, body })
end

local function div(el)
  if el.classes:includes("capture") then return capture_div(el) end
  if el.identifier:match("^tbl:") then return table_div(el) end
  for class, _ in pairs(callout_titles) do
    if el.classes:includes(class) then
      local out = callout_div(el, class)
      if out then return out end
      return nil
    end
  end
  return nil
end

-- References --------------------------------------------------------

local function cite(el)
  local id = el.citations[1].id
  local label = labels[id]
  if label then return pandoc.Str(label) end
  if id:match("^fig:") or id:match("^lst:") or id:match("^tbl:") then
    io.stderr:write("WARNING: unresolved reference @" .. id .. "\n")
    return pandoc.Strong({ pandoc.Str("??" .. id) })
  end
  return nil
end

-- DOCX chapter opener -----------------------------------------------

local function docx_opener(doc)
  if not is_docx then return nil end
  local m = doc.meta
  local blocks = pandoc.Blocks({})
  if m.part then
    blocks:insert(styled({ pandoc.Para(md_inlines(stringify(m.part))) }, "Opener Part"))
  end
  if m["build-summary"] then
    blocks:insert(styled({ pandoc.Para(md_inlines(stringify(m["build-summary"]))) },
      "Opener Summary"))
  end
  if m.builds then
    local items = {}
    for _, b in ipairs(m.builds) do
      table.insert(items, { pandoc.Plain(md_inlines(stringify(b))) })
    end
    blocks:insert(styled({ pandoc.Para({ pandoc.Str("What you’ll build") }) },
      "Opener Builds Label"))
    blocks:insert(styled({ pandoc.BulletList(items) }, "Opener Builds"))
  end
  if m.versions then
    blocks:insert(styled({ pandoc.Para(md_inlines("Tested with: " ..
      stringify(m.versions))) }, "Opener Versions"))
  end
  local kind = m.appendix and "Appendix " or "Chapter "
  m.subtitle = pandoc.MetaInlines({ pandoc.Str(kind .. chapter) })
  blocks:extend(doc.blocks)
  return pandoc.Pandoc(blocks, m)
end

-- Filter order ------------------------------------------------------

return {
  {
    Meta = function(m)
      if m.chapter then chapter = stringify(m.chapter) end
      if m["code-root"] then code_root = stringify(m["code-root"]) end
      local n = tonumber(chapter)
      if n then
        m["chapter-padded"] = pandoc.MetaString(string.format("%02d", n))
      else
        m["chapter-padded"] = pandoc.MetaString(chapter)
      end
      return m
    end,
  },
  {
    traverse = "topdown",
    CodeBlock = code_block,
    Figure = figure,
    Div = div,
  },
  { Cite = cite },
  { Pandoc = docx_opener },
}
