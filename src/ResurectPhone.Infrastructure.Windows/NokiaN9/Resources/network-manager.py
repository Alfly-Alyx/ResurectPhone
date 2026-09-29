# -*- coding: utf-8 -*-
# Harmattan network manager. Uses the Nokia libconnsettings and ICD2 APIs.
# Python 2.6 / Python 3 compatible. Wi-Fi passwords are never read or reported.
from __future__ import print_function
import ctypes as c
import json, os, sys, time, base64, socket, struct, fcntl

try: text_types=(basestring,)
except NameError: text_types=(str,)

P = c.c_void_p
CONFIG = '/home/user/.config/resurectphone/wifi.json'
STATE = '/home/user/.cache/resurectphone/wifi-state.json'
WLAN = 'WLAN_INFRA'
IAPNAME = 0x01000000

def enc(value):
    return value.encode('utf-8') if not isinstance(value, bytes) else value

def dec(value):
    return (value or b'').decode('utf-8', 'replace')

def bind(lib, name, result, *args):
    func = getattr(lib, name); func.restype = result; func.argtypes = list(args)
    return func

def atomic_json(path, data):
    directory = os.path.dirname(path)
    if not os.path.isdir(directory): os.makedirs(directory, 0o700)
    temporary = path + '.%s.new' % os.getpid()
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, 'w') as out: json.dump(data, out)
        os.rename(temporary, path)
    finally:
        if os.path.exists(temporary): os.unlink(temporary)

def read_json(path, default):
    if not os.path.exists(path): return default
    with open(path) as data: return json.load(data)

class SettingData(c.Union): pass
class Setting(c.Structure): pass
class ByteArray(c.Structure):
    _fields_ = [('value', P), ('length', c.c_uint)]
SettingData._fields_ = [('string', c.c_char_p), ('integer', c.c_int), ('real', c.c_double),
                       ('boolean', c.c_int), ('bytes', ByteArray), ('items', c.POINTER(c.POINTER(Setting)))]
Setting._fields_ = [('type', c.c_int), ('value', SettingData), ('data', P)]

class Settings(object):
    def __init__(self):
        self.lib = c.CDLL('libconnsettings.so.0'); self.glib = c.CDLL('libglib-2.0.so.0')
        self.open = bind(self.lib, 'conn_settings_open', P, c.c_int, c.c_char_p)
        self.close = bind(self.lib, 'conn_settings_close', None, P)
        self.get = bind(self.lib, 'conn_settings_get', c.POINTER(Setting), P, c.c_char_p)
        self.destroy = bind(self.lib, 'conn_settings_value_destroy', None, c.POINTER(Setting))
        self.ids = bind(self.lib, 'conn_settings_list_ids', c.POINTER(c.c_char_p), c.c_int)
        self.free_ids = bind(self.glib, 'g_strfreev', None, c.POINTER(c.c_char_p))
    def unpack(self, item):
        t = item.type; v = item.value
        if t == 1: value = dec(v.string)
        elif t == 2: value = v.integer
        elif t == 4: value = bool(v.boolean)
        elif t == 6: value = list(bytearray(c.string_at(v.bytes.value, v.bytes.length)))
        elif t == 5:
            value = []; index = 0
            while v.items and v.items[index]:
                value.append(self.unpack(v.items[index].contents)); index += 1
        else: raise RuntimeError('Type de reglage reseau non pris en charge')
        return {'type':t, 'value':value}
    def read(self, context, identity, key):
        handle = self.open(context, enc(identity) if identity is not None else None)
        if not handle: raise RuntimeError('Reglages reseau indisponibles')
        try:
            value = self.get(handle, enc(key))
            if not value: return None
            try: return self.unpack(value.contents)
            finally: self.destroy(value)
        finally: self.close(handle)
    def write(self, context, identity, key, value):
        handle = self.open(context, enc(identity) if identity is not None else None)
        if not handle: raise RuntimeError('Reglages reseau indisponibles')
        try:
            if value is None: result = bind(self.lib,'conn_settings_unset',c.c_int,P,c.c_char_p)(handle,enc(key))
            elif value['type'] in (2,4):
                method = 'conn_settings_set_bool' if value['type'] == 4 else 'conn_settings_set_int'
                result = bind(self.lib,method,c.c_int,P,c.c_char_p,c.c_int)(handle,enc(key),int(value['value']))
            elif value['type'] == 5:
                values = value['value']
                if any(x['type'] != 1 for x in values): raise RuntimeError('Liste reseau inattendue')
                raw = (c.c_char_p * len(values))(*[enc(x['value']) for x in values])
                result = bind(self.lib,'conn_settings_set_list',c.c_int,P,c.c_char_p,P,c.c_uint,c.c_int)(handle,enc(key),raw,len(values),1)
            else: raise RuntimeError('Ecriture de ce type interdite')
            if result != 0: raise RuntimeError('Modification du reglage refusee (%s)' % result)
        finally: self.close(handle)
        if self.read(context,identity,key) != value: raise RuntimeError('Le reglage reseau ne conserve pas la valeur demandee')
    def profiles(self):
        ids = self.ids(3); result = []
        try:
            index = 0
            while ids and ids[index]:
                identity = dec(ids[index]); index += 1
                def get(key, default=None):
                    v = self.read(3,identity,key)
                    return v['value'] if v is not None else default
                if get('type') != WLAN: continue
                ssid = get('wlan_ssid', [])
                ssid = [v['value'] if isinstance(v,dict) else v for v in ssid]
                auto = self.read(3,identity,'autoconnect')
                result.append({'id':identity, 'name':get('name',identity), 'ssid':ssid,
                               'automatic':auto['value'] if auto and auto['type']==4 else None,
                               'security':get('wlan_security','Inconnue')})
        finally:
            if ids: self.free_ids(ids)
        return result
    def require(self, identity):
        matches = [p for p in self.profiles() if p['id'] == identity]
        if len(matches) != 1: raise RuntimeError('Ce profil Wi-Fi enregistre est introuvable')
        return matches[0]

class Bus(object):
    def __init__(self):
        lib = self.lib = c.CDLL('libdbus-1.so.3')
        self.bus = bind(lib,'dbus_bus_get_private',P,c.c_int,P)(1,None)
        if not self.bus: raise RuntimeError('Bus systeme indisponible')
        self.unref = bind(lib,'dbus_message_unref',None,P)
        self.init = bind(lib,'dbus_message_iter_init',c.c_int,P,P)
        self.next = bind(lib,'dbus_message_iter_next',c.c_int,P)
        self.kind = bind(lib,'dbus_message_iter_get_arg_type',c.c_int,P)
        self.basic = bind(lib,'dbus_message_iter_get_basic',None,P,P)
        self.recurse = bind(lib,'dbus_message_iter_recurse',None,P,P)
    def storage(self): return (P * 32)()
    def decode(self, iterator):
        values=[]
        while self.kind(iterator):
            kind = chr(self.kind(iterator))
            if kind in ('s','o','g'): value=c.c_char_p(); self.basic(iterator,c.byref(value)); value=dec(value.value)
            elif kind in ('u','b','i','y'):
                value={'u':c.c_uint32,'b':c.c_uint32,'i':c.c_int32,'y':c.c_ubyte}[kind]()
                self.basic(iterator,c.byref(value)); value=value.value
            elif kind in ('a','r','v'):
                child=self.storage(); self.recurse(iterator,child); value=self.decode(child)
            else: raise RuntimeError('Reponse reseau inattendue')
            values.append(value)
            if not self.next(iterator): break
        return values
    def append(self, iterator, kind, value):
        if kind in ('s','u','y'):
            raw={'s':c.c_char_p,'u':c.c_uint32,'y':c.c_ubyte}[kind](enc(value) if kind=='s' else value)
            if not bind(self.lib,'dbus_message_iter_append_basic',c.c_int,P,c.c_int,P)(iterator,ord(kind),c.byref(raw)): raise MemoryError()
        elif kind in ('as','ay'):
            child=self.storage()
            if not bind(self.lib,'dbus_message_iter_open_container',c.c_int,P,c.c_int,c.c_char_p,P)(iterator,ord('a'),enc(kind[1]),child): raise MemoryError()
            for item in value: self.append(child,kind[1],item)
            if not bind(self.lib,'dbus_message_iter_close_container',c.c_int,P,P)(iterator,child): raise MemoryError()
        else: raise RuntimeError('Argument reseau interdit')
    def call(self, service, path, interface, method, args=()):
        message=bind(self.lib,'dbus_message_new_method_call',P,c.c_char_p,c.c_char_p,c.c_char_p,c.c_char_p)(enc(service),enc(path),enc(interface),enc(method))
        if not message: raise MemoryError()
        try:
            it=self.storage(); bind(self.lib,'dbus_message_iter_init_append',None,P,P)(message,it)
            for kind,value in args: self.append(it,kind,value)
            error=(P*16)(); bind(self.lib,'dbus_error_init',None,P)(error)
            reply=bind(self.lib,'dbus_connection_send_with_reply_and_block',P,P,P,c.c_int,P)(self.bus,message,10000,error)
            if not reply:
                name=c.cast(error,c.POINTER(c.c_char_p))[0]
                text='Service reseau indisponible : '+dec(name)
                bind(self.lib,'dbus_error_free',None,P)(error)
                raise RuntimeError(text)
            try:
                it=self.storage()
                return self.decode(it) if self.init(reply,it) else []
            finally: self.unref(reply)
        finally: self.unref(message)
    def icd(self, method, args=()): return self.call('com.nokia.icd2','/com/nokia/icd2','com.nokia.icd2',method,args)
    def signals(self, seconds, stop=None):
        until=time.time()+seconds; result=[]
        read=bind(self.lib,'dbus_connection_read_write',c.c_int,P,c.c_int)
        pop=bind(self.lib,'dbus_connection_pop_message',P,P)
        member=bind(self.lib,'dbus_message_get_member',c.c_char_p,P)
        interface=bind(self.lib,'dbus_message_get_interface',c.c_char_p,P)
        while time.time()<until:
            read(self.bus,100)
            while True:
                message=pop(self.bus)
                if not message: break
                try:
                    if dec(interface(message))!='com.nokia.icd2': continue
                    it=self.storage(); args=self.decode(it) if self.init(message,it) else []
                    item=(dec(member(message)),args); result.append(item)
                    if stop and stop(item): return result
                finally: self.unref(message)
        return result
    def states(self):
        count=self.icd('state_req')[0]
        if not count: return []
        result=[]
        def received(item):
            if item[0]=='state_sig' and len(item[1])==8: result.append(item[1])
            return len(result)>=count
        self.signals(3,received); return result
    def radios(self):
        return self.call('com.nokia.mce','/com/nokia/mce/request','com.nokia.mce.request','get_radio_states')[0]
    def power_saving(self):
        return bool(self.call('com.nokia.mce','/com/nokia/mce/request','com.nokia.mce.request','get_psm_state')[0])

def same_network(profile,state):
    if len(state)!=8 or state[3]!=WLAN: return False
    raw=bytes(bytearray(state[5]))
    return raw.rstrip(b'\x00')==enc(profile['id']) or list(bytearray(raw))==profile['ssid']

def retry_delay(failures): return min(300,30*(2**min(max(failures,0),4)))

def can_retry(radios, states, profiles, identity):
    if radios & 5 != 5: return False
    if any(len(s)==8 and s[3]==WLAN and s[7] in (1,2,15) for s in states): return False
    return any(p['id']==identity and p['automatic'] is True for p in profiles)

def connection(identity, settings, bus):
    profile=settings.require(identity)
    if bus.radios() & 5 != 5: raise RuntimeError('Wi-Fi eteint ou mode avion actif sur le N9')
    if any(same_network(profile,s) and s[7]==2 for s in bus.states()): return
    # Public libconic API: UNMANAGED keeps the request after this process exits.
    glib=c.CDLL('libglib-2.0.so.0'); obj=c.CDLL('libgobject-2.0.so.0'); conic=c.CDLL('libconic.so.0')
    bind(obj,'g_type_init',None)()
    shared=bind(bus.lib,'dbus_bus_get',P,c.c_int,P)(1,None)
    if not shared: raise RuntimeError('Bus de connexion indisponible')
    bind(c.CDLL('libdbus-glib-1.so.2'),'dbus_connection_setup_with_g_main',None,P,P)(shared,None)
    client=bind(conic,'con_ic_connection_new',P)()
    if not client: raise RuntimeError('Impossible de creer la demande Wi-Fi')
    try:
        if not bind(conic,'con_ic_connection_connect_by_id',c.c_int,P,c.c_char_p,c.c_int)(client,enc(identity),2):
            raise RuntimeError('Demande de connexion Wi-Fi refusee')
        iterate=bind(glib,'g_main_context_iteration',c.c_int,P,c.c_int)
        # Status replies must be read by a separate private bus while libconic dispatches.
        until=time.time()+55
        while time.time()<until:
            while iterate(None,0): pass
            time.sleep(0.2)
            if wifi_address():
                # Verify the specific network, not just an existing interface address.
                if any(same_network(profile,s) and s[7]==2 for s in bus.states()): return
        raise RuntimeError('Le reseau choisi ne confirme pas sa connexion (hors de portee ou authentification refusee)')
    finally:
        bind(obj,'g_object_unref',None,P)(client)
        bind(bus.lib,'dbus_connection_unref',None,P)(shared)

def wifi_address():
    sock=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
    try:
        return socket.inet_ntoa(fcntl.ioctl(sock.fileno(),0x8915,struct.pack('256s',b'wlan0'))[20:24])
    except IOError: return ''
    finally: sock.close()

def status(settings,bus,scan=False):
    profiles=settings.profiles(); states=bus.states(); config=read_json(CONFIG,{'forced':[]})
    for p in profiles:
        p['connected']=any(same_network(p,s) and s[7]==2 for s in states)
        p['forced']=p['id'] in config.get('forced',[])
    available=[]
    if scan:
        bus.icd('scan_req',[('u',0),('as',[WLAN])])
        try:
            def complete(item): return item[0]=='scan_result_sig' and len(item[1])>=1 and item[1][0]==4
            for member,args in bus.signals(15,complete):
                if member=='scan_result_sig' and len(args)==15 and args[0] in (0,1) and args[7]==WLAN:
                    available.append({'name':args[8],'signal':args[12]})
        finally: bus.icd('scan_cancel_req')
    def get(key):
        value=settings.read(2,None,key); return value['value'] if value else None
    return {'profiles':profiles,'available':available,'address':wifi_address(),'radios':bus.radios(),
            'powerSaving':bus.power_saving(),'searchInterval':get('search_interval'),
            'restricted':get('restricted_mode'),'daemon':read_json(STATE,{}),
            'globalAuto':get('auto_connect')}

def automatic(settings,identity,enabled,forced,backup):
    profile=settings.require(identity)
    keys=[(3,identity,'autoconnect')]
    if enabled: keys.extend([(2,None,'auto_connect'),(2,None,'search_interval')])
    snapshot={'settings':[[t,i,k,settings.read(t,i,k)] for t,i,k in keys], 'config':read_json(CONFIG,None)}
    atomic_json(backup,snapshot)
    try:
        settings.write(3,identity,'autoconnect',{'type':4,'value':bool(enabled)})
        if enabled:
            auto=settings.read(2,None,'auto_connect')
            if auto is not None and auto['type']!=5: raise RuntimeError('Politique reseau inconnue')
            values=auto['value'] if auto else []
            if not any(v['value'] in ('*',WLAN) for v in values):
                settings.write(2,None,'auto_connect',{'type':5,'value':values+[{'type':1,'value':WLAN}]})
            interval=settings.read(2,None,'search_interval')
            if interval is None or interval['type']!=2 or interval['value']<=0 or interval['value']>60:
                settings.write(2,None,'search_interval',{'type':2,'value':60})
        config=read_json(CONFIG,{'forced':[]})
        config['forced']=[p for p in config.get('forced',[]) if p!=identity]
        if enabled and forced: config['forced'].append(identity)
        atomic_json(CONFIG,config)
    except Exception:
        restore(settings,backup); raise

def restore(settings,path):
    snapshot=read_json(path,None)
    if not snapshot: raise RuntimeError('Sauvegarde Wi-Fi manquante')
    for t,i,k,value in snapshot['settings']:
        if not ((t==3 and k=='autoconnect' and isinstance(i,text_types)) or (t==2 and i is None and k in ('auto_connect','search_interval'))):
            raise RuntimeError('Sauvegarde Wi-Fi non reconnue')
        if t==3: settings.require(i)
    for t,i,k,value in snapshot['settings']: settings.write(t,i,k,value)
    if snapshot['config'] is None:
        if os.path.exists(CONFIG): os.unlink(CONFIG)
    else: atomic_json(CONFIG,snapshot['config'])

def daemon(settings,bus):
    import signal
    running=[True]
    signal.signal(signal.SIGTERM,lambda *unused: running.__setitem__(0,False))
    failures=0; due=0
    while running[0]:
        try:
            config=read_json(CONFIG,{'forced':[]}); forced=config.get('forced',[])
            profiles=settings.profiles(); states=bus.states(); radios=bus.radios()
            connected=any(len(s)==8 and s[3]==WLAN and s[7]==2 for s in states)
            if connected: failures=0; due=0
            phase='connected' if connected else 'waiting'
            if radios & 5 != 5: phase='radio-off'
            if not forced: phase='disabled'
            for identity in forced:
                if time.time()<due or not can_retry(radios,states,profiles,identity): continue
                phase='connecting'; atomic_json(STATE,{'phase':phase,'at':time.time(),'failures':failures})
                try: connection(identity,settings,bus); failures=0; phase='connected'
                except Exception: failures+=1; phase='retrying'
                due=time.time()+retry_delay(failures)
                break
            atomic_json(STATE,{'phase':phase,'at':time.time(),'failures':failures})
        except Exception:
            failures+=1
            try: atomic_json(STATE,{'phase':'error','at':time.time(),'failures':failures})
            except Exception: pass
        # Bounded retries; no scanning while already connected, no mobile fallback.
        for tick in range(30):
            if not running[0]: break
            time.sleep(1)

def main():
    request=json.loads(dec(base64.b64decode(enc(sys.argv[1])))) if len(sys.argv)>1 and sys.argv[1]!='daemon' else {'action':'daemon'}
    settings=Settings(); action=request['action']
    if action=='restore': restore(settings,request['backup']); print('{}'); return
    if action=='automatic':
        automatic(settings,request['id'],request['enabled'],request['forced'],request['backup']); print('{}'); return
    bus=Bus()
    if action=='daemon': daemon(settings,bus); return
    if action=='connect': connection(request['id'],settings,bus)
    elif action=='disconnect':
        profile=settings.require(request['id'])
        for state in bus.states():
            if same_network(profile,state) and state[7] in (1,2,15):
                bus.icd('disconnect_req',[('u',1),('s',state[0]),('u',state[1]),('s',state[2]),('s',state[3]),('u',state[4]),('ay',state[5])])
        until=time.time()+10
        while time.time()<until:
            if not any(same_network(profile,s) and s[7] in (1,2,15) for s in bus.states()): break
            time.sleep(0.5)
    elif action not in ('status','scan'): raise RuntimeError('Action reseau inconnue')
    print(json.dumps(status(settings,bus,action=='scan')))

if __name__=='__main__':
    try: main()
    except Exception as error:
        print(json.dumps({'error':str(error)})); sys.exit(1)
