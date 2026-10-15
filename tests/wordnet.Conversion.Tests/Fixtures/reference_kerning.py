"""Independent reference shaping with the explicitly supplied, pinned HarfBuzz tool.

This helper is used only by fixture generation, never by production wordnet.
"""
import ctypes as c
import os


class Feature(c.Structure):
    _fields_ = [('tag', c.c_uint32), ('value', c.c_uint32), ('start', c.c_uint32), ('end', c.c_uint32)]


class Info(c.Structure):
    _fields_ = [('codepoint', c.c_uint32), ('mask', c.c_uint32), ('cluster', c.c_uint32), ('var1', c.c_uint32), ('var2', c.c_uint32)]


class Position(c.Structure):
    _fields_ = [('x_advance', c.c_int32), ('y_advance', c.c_int32), ('x_offset', c.c_int32), ('y_offset', c.c_int32), ('var', c.c_uint32)]


class ReferenceShaper:
    def __init__(self, fonts):
        self.library = c.CDLL(os.environ['WORDNET_HARFBUZZ_LIBRARY'])
        definitions = {
            'hb_blob_create': (c.c_void_p, [c.c_char_p, c.c_uint, c.c_int, c.c_void_p, c.c_void_p]),
            'hb_face_create': (c.c_void_p, [c.c_void_p, c.c_uint]),
            'hb_face_get_upem': (c.c_uint, [c.c_void_p]),
            'hb_font_create': (c.c_void_p, [c.c_void_p]),
            'hb_ot_font_set_funcs': (None, [c.c_void_p]),
            'hb_font_set_scale': (None, [c.c_void_p, c.c_int, c.c_int]),
            'hb_buffer_create': (c.c_void_p, []),
            'hb_buffer_add_utf32': (None, [c.c_void_p, c.POINTER(c.c_uint32), c.c_int, c.c_uint, c.c_int]),
            'hb_buffer_guess_segment_properties': (None, [c.c_void_p]),
            'hb_feature_from_string': (c.c_int, [c.c_char_p, c.c_int, c.POINTER(Feature)]),
            'hb_shape': (None, [c.c_void_p, c.c_void_p, c.POINTER(Feature), c.c_uint]),
            'hb_buffer_get_glyph_infos': (c.POINTER(Info), [c.c_void_p, c.POINTER(c.c_uint)]),
            'hb_buffer_get_glyph_positions': (c.POINTER(Position), [c.c_void_p, c.POINTER(c.c_uint)]),
            'hb_buffer_destroy': (None, [c.c_void_p]),
            'hb_font_destroy': (None, [c.c_void_p]),
            'hb_face_destroy': (None, [c.c_void_p]),
            'hb_blob_destroy': (None, [c.c_void_p]),
            'hb_version_string': (c.c_char_p, []),
        }
        for name, (result, arguments) in definitions.items():
            function = getattr(self.library, name); function.restype = result; function.argtypes = arguments
        self.faces = {}
        for name, path in fonts.items():
            data = path.read_bytes()
            blob = self.library.hb_blob_create(data, len(data), 0, None, None)
            face = self.library.hb_face_create(blob, 0)
            units = self.library.hb_face_get_upem(face)
            font = self.library.hb_font_create(face)
            self.library.hb_ot_font_set_funcs(font)
            self.library.hb_font_set_scale(font, units, units)
            self.faces[name] = (font, face, blob, units)
        self.version = self.library.hb_version_string().decode('ascii')

    def shape(self, text, name, size):
        font, _, _, units = self.faces[name]
        buffer = self.library.hb_buffer_create()
        try:
            scalars = (c.c_uint32 * len(text))(*map(ord, text))
            self.library.hb_buffer_add_utf32(buffer, scalars, len(text), 0, len(text))
            self.library.hb_buffer_guess_segment_properties(buffer)
            # Isolate pair positioning; contextual substitutions/ligatures are not
            # the layout profile measured by this reference corpus.
            features = (Feature * 4)()
            for i, flag in enumerate((b'kern=1', b'liga=0', b'clig=0', b'calt=0')):
                if not self.library.hb_feature_from_string(flag, -1, c.byref(features[i])):
                    raise ValueError('Could not configure reference features')
            self.library.hb_shape(font, buffer, features, len(features))
            length = c.c_uint()
            infos = self.library.hb_buffer_get_glyph_infos(buffer, c.byref(length))
            positions = self.library.hb_buffer_get_glyph_positions(buffer, c.byref(length))
            if length.value != len(text) or any(infos[i].cluster != i for i in range(length.value)):
                raise ValueError('Reference text unexpectedly required contextual substitution')
            result = []; x = y = 0
            for i, character in enumerate(text):
                p = positions[i]
                result.append((character, (x + p.x_offset) * size / units, (y + p.y_offset) * size / units))
                x += p.x_advance; y += p.y_advance
            return result, x * size / units
        finally:
            self.library.hb_buffer_destroy(buffer)

    def close(self):
        for font, face, blob, _ in self.faces.values():
            self.library.hb_font_destroy(font); self.library.hb_face_destroy(face); self.library.hb_blob_destroy(blob)
        self.faces.clear()
