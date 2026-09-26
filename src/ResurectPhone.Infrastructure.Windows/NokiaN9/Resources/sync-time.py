# Set the Harmattan timed wall clock through its public D-Bus API.
# Wire layout: sailfishos/timed src/lib/wall-settings.cpp, WallOpcode manual=4.
import ctypes as c, sys, time
lib = c.CDLL('libdbus-1.so.3')
p = c.c_void_p

def fn(name, restype, *args):
    f = getattr(lib, name)
    f.restype = restype
    f.argtypes = list(args)
    return f

bus_get = fn('dbus_bus_get', p, c.c_int, p)
new_call = fn('dbus_message_new_method_call', p, c.c_char_p, c.c_char_p, c.c_char_p, c.c_char_p)
init_append = fn('dbus_message_iter_init_append', None, p, p)
open_container = fn('dbus_message_iter_open_container', c.c_int, p, c.c_int, c.c_char_p, p)
close_container = fn('dbus_message_iter_close_container', c.c_int, p, p)
append_basic = fn('dbus_message_iter_append_basic', c.c_int, p, c.c_int, p)
call = fn('dbus_connection_send_with_reply_and_block', p, p, p, c.c_int, p)
iter_init = fn('dbus_message_iter_init', c.c_int, p, p)
get_type = fn('dbus_message_iter_get_arg_type', c.c_int, p)
get_basic = fn('dbus_message_iter_get_basic', None, p, p)
unref = fn('dbus_message_unref', None, p)

class Timespec(c.Structure):
    _fields_ = [('seconds', c.c_long), ('nanoseconds', c.c_long)]
mono = Timespec()
librt = c.CDLL('librt.so.1')
if librt.clock_gettime(1, c.byref(mono)) != 0: raise RuntimeError('Monotonic clock unavailable')
target = int(sys.argv[1])
seconds, nanos = target - mono.seconds, -mono.nanoseconds
if nanos < 0: seconds, nanos = seconds - 1, nanos + 1000000000
bus = bus_get(1, None)
if not bus: raise RuntimeError('System bus unavailable')
message = new_call(b'com.nokia.time', b'/com/nokia/time', b'com.nokia.time', b'wall_clock_settings')
if not message: raise RuntimeError('D-Bus message allocation failed')
# Aligned storage larger than DBusMessageIter on both ARM32 and 64-bit hosts.
root, outer, instant = (c.c_void_p * 32)(), (c.c_void_p * 32)(), (c.c_void_p * 32)()
def append(iterator, kind, value):
    if not append_basic(iterator, ord(kind), c.byref(value)): raise RuntimeError('D-Bus argument allocation failed')
try:
    init_append(message, root)
    if not open_container(root, ord('r'), None, outer): raise RuntimeError('D-Bus struct allocation failed')
    append(outer, 'u', c.c_uint32(4))
    if not open_container(outer, ord('r'), None, instant): raise RuntimeError('D-Bus time allocation failed')
    append(instant, 'i', c.c_int32(seconds))
    append(instant, 'u', c.c_uint32(nanos))
    if not close_container(outer, instant): raise RuntimeError('D-Bus time completion failed')
    append(outer, 'i', c.c_int32(0))
    append(outer, 's', c.c_char_p(b''))
    if not close_container(root, outer): raise RuntimeError('D-Bus struct completion failed')
    error = (c.c_void_p * 16)()
    fn('dbus_error_init', None, p)(error)
    reply = call(bus, message, 10000, error)
    if not reply:
        detail = c.cast(error, c.POINTER(c.c_char_p))
        raise RuntimeError(str(detail[0]) + ': ' + str(detail[1]))
    try:
        result = c.c_int()
        if not iter_init(reply, root) or get_type(root) != ord('b'): raise RuntimeError('Unexpected timed response')
        get_basic(root, c.byref(result))
        if not result.value: raise RuntimeError('Timed rejected the wall clock request')
    finally: unref(reply)
finally: unref(message)
# The daemon applies settings asynchronously; allow its notification to arrive.
for attempt in range(20):
    if abs(time.time() - target) < 10:
        print('Heure du N9 synchronisee et verifiee via timed.')
        break
    time.sleep(0.1)
else: raise RuntimeError('Clock did not adopt the requested time')
